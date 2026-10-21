using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;

namespace MsiInspector;

public partial class MainWindow : Window
{
    readonly ObservableCollection<MsiPackage> packages = new();
    readonly ObservableCollection<CustomActionEntry> customActions = new();
    readonly ICollectionView packageView;
    readonly ICollectionView customActionView;
    CancellationTokenSource? scanCancel;

    string? duplicateField; // ProductCode, UpgradeCode, PackageCode, or null for all files
    HashSet<string> duplicateValues = new(StringComparer.OrdinalIgnoreCase);
    string? sharedActionFilter; // custom action name picked in the "shared" list

    public MainWindow()
    {
        InitializeComponent();

        packageView = CollectionViewSource.GetDefaultView(packages);
        packageView.Filter = PackageFilter;
        PackageGrid.ItemsSource = packageView;

        customActionView = CollectionViewSource.GetDefaultView(customActions);
        customActionView.Filter = CustomActionFilter;
        CustomActionGrid.ItemsSource = customActionView;
    }

    /* ---------- Scanning ---------- */

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to scan for MSI files" };
        if (Directory.Exists(FolderBox.Text)) dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
            Scan_Click(sender, e);
        }
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        string root = FolderBox.Text.Trim().Trim('"');
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, "That folder doesn't exist. Choose a folder to scan.", "MSI Inspector",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        scanCancel = new CancellationTokenSource();
        var token = scanCancel.Token;
        SetScanning(true);
        packages.Clear();
        customActions.Clear();
        SharedGrid.ItemsSource = null;
        ClearDuplicateMode();
        StatusText.Text = "Looking for MSI files...";

        try
        {
            var files = await Task.Run(() => FindMsiFiles(root), token);
            Progress.Maximum = Math.Max(1, files.Count);
            Progress.Value = 0;

            var progress = new Progress<(int Done, MsiPackage Package, List<CustomActionEntry> Actions)>(p =>
            {
                packages.Add(p.Package);
                foreach (var ca in p.Actions) customActions.Add(ca);
                Progress.Value = p.Done;
                StatusText.Text = $"Reading {p.Done} of {files.Count}: {p.Package.FileName}";
            });

            await Task.Run(() =>
            {
                IProgress<(int, MsiPackage, List<CustomActionEntry>)> report = progress;
                for (int i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var (pkg, actions) = MsiReader.Read(files[i]);
                    report.Report((i + 1, pkg, actions));
                }
            }, token);

            StatusText.Text = SummaryText();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = $"Scan cancelled. {SummaryText()}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Scan failed: " + ex.Message;
        }
        finally
        {
            SetScanning(false);
            BuildSharedList();
        }
    }

    static List<string> FindMsiFiles(string root)
    {
        // Skip folders we aren't allowed into instead of failing the whole scan
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        return Directory.EnumerateFiles(root, "*.msi", options).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => scanCancel?.Cancel();

    void SetScanning(bool scanning)
    {
        ScanButton.IsEnabled = !scanning;
        BrowseButton.IsEnabled = !scanning;
        CancelButton.IsEnabled = scanning;
        Progress.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
    }

    string SummaryText()
    {
        int failed = packages.Count(p => p.Error.Length > 0);
        return $"{packages.Count} MSI files, {customActions.Count} custom actions" +
               (failed > 0 ? $", {failed} could not be read (see the Problem column)" : "") + ".";
    }

    /* ---------- MSI grid: filter and duplicates ---------- */

    bool PackageFilter(object item)
    {
        var p = (MsiPackage)item;
        if (duplicateField != null && !duplicateValues.Contains(CodeOf(p, duplicateField))) return false;
        string f = FilterBox.Text.Trim();
        if (f.Length == 0) return true;
        return new[] { p.FileName, p.ProductName, p.ProductVersion, p.Manufacturer, p.ProductCode, p.UpgradeCode, p.PackageCode, p.Language, p.Folder, p.Error }
            .Any(v => v.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    static string CodeOf(MsiPackage p, string field) => field switch
    {
        "ProductCode" => p.ProductCode,
        "UpgradeCode" => p.UpgradeCode,
        "PackageCode" => p.PackageCode,
        _ => "",
    };

    void Filter_TextChanged(object sender, TextChangedEventArgs e) => packageView.Refresh();

    void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        string field = (string)button.Tag;
        if (button.IsChecked != true)
        {
            ClearDuplicateMode();
            return;
        }

        foreach (var b in new[] { DupProductCode, DupUpgradeCode, DupPackageCode }) b.IsChecked = b == button;
        duplicateField = field;

        // Codes that appear on more than one file
        var groups = packages.Where(p => CodeOf(p, field).Length > 0)
            .GroupBy(p => CodeOf(p, field), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        duplicateValues = new HashSet<string>(groups.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var p in packages) p.DuplicateCount = "";
        foreach (var g in groups) foreach (var p in g) p.DuplicateCount = g.Count().ToString();

        // Group matching files together by sorting on the code
        packageView.SortDescriptions.Clear();
        packageView.SortDescriptions.Add(new SortDescription(field, ListSortDirection.Ascending));
        packageView.SortDescriptions.Add(new SortDescription(nameof(MsiPackage.FileName), ListSortDirection.Ascending));
        foreach (var col in PackageGrid.Columns) col.SortDirection = null;
        DuplicateColumn.Visibility = Visibility.Visible;
        packageView.Refresh();

        int files = groups.Sum(g => g.Count());
        StatusText.Text = groups.Count == 0
            ? $"No duplicate {field}s found."
            : $"{files} files share {groups.Count} {field}{(groups.Count == 1 ? "" : "s")}.";
    }

    void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Text = "";
        ClearDuplicateMode();
        StatusText.Text = SummaryText();
    }

    void ClearDuplicateMode()
    {
        duplicateField = null;
        duplicateValues.Clear();
        foreach (var b in new[] { DupProductCode, DupUpgradeCode, DupPackageCode }) b.IsChecked = false;
        DuplicateColumn.Visibility = Visibility.Collapsed;
        packageView.SortDescriptions.Clear();
        packageView.Refresh();
    }

    /* ---------- Custom actions: shared list and search ---------- */

    void BuildSharedList()
    {
        var shared = customActions
            .GroupBy(ca => ca.Action, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SharedCustomAction
            {
                Action = g.Key,
                MsiCount = g.Select(ca => ca.Package).Distinct().Count(),
                // How many different implementations share this name (type + source + target)
                Variants = g.Select(ca => $"{ca.Type}|{ca.Source}|{ca.Target}").Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            })
            .Where(s => s.MsiCount > 1)
            .OrderByDescending(s => s.MsiCount)
            .ThenBy(s => s.Action, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SharedGrid.ItemsSource = shared;
    }

    bool CustomActionFilter(object item)
    {
        var ca = (CustomActionEntry)item;
        if (sharedActionFilter != null && !ca.Action.Equals(sharedActionFilter, StringComparison.OrdinalIgnoreCase)) return false;
        string q = CaSearchBox.Text.Trim();
        if (q.Length == 0) return true;
        if (CaExactBox.IsChecked == true) return ca.Action.Equals(q, StringComparison.OrdinalIgnoreCase);
        return ca.Action.Contains(q, StringComparison.OrdinalIgnoreCase)
            || ca.Source.Contains(q, StringComparison.OrdinalIgnoreCase)
            || ca.Target.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    void SharedGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        sharedActionFilter = (SharedGrid.SelectedItem as SharedCustomAction)?.Action;
        if (sharedActionFilter != null) CaSearchBox.Text = "";
        customActionView.Refresh();
        UpdateCustomActionStatus();
    }

    void CaSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CaSearchBox.Text.Length > 0 && SharedGrid.SelectedItem != null)
        {
            SharedGrid.SelectedItem = null; // typing a search replaces the shared-list pick
            sharedActionFilter = null;
        }
        customActionView.Refresh();
        UpdateCustomActionStatus();
    }

    void CaSearch_Changed(object sender, RoutedEventArgs e) => CaSearch_TextChanged(sender, null!);

    void UpdateCustomActionStatus()
    {
        var shown = customActionView.Cast<CustomActionEntry>().ToList();
        int msis = shown.Select(ca => ca.Package).Distinct().Count();
        StatusText.Text = $"{shown.Count} custom actions shown, across {msis} MSI file{(msis == 1 ? "" : "s")}.";
    }

    /* ---------- Copy, open, export ---------- */

    DataGrid? GridFor(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu menu }) return menu.PlacementTarget as DataGrid;
        return sender as DataGrid;
    }

    object? CurrentItem(DataGrid grid) =>
        grid.CurrentCell.Item is MsiPackage or CustomActionEntry ? grid.CurrentCell.Item : grid.SelectedItem;

    void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        var grid = GridFor(sender);
        if (grid == null || CurrentItem(grid) is not { } item) return;
        var values = grid.Columns.Where(c => c.Visibility == Visibility.Visible)
            .Select(c => (c.GetCellContent(item) as TextBlock)?.Text ?? "");
        Clipboard.SetText(string.Join("\t", values));
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var grid = GridFor(sender);
        if (grid != null) RevealInExplorer(CurrentItem(grid));
    }

    void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid) RevealInExplorer(CurrentItem(grid));
    }

    static void RevealInExplorer(object? item)
    {
        string? path = item switch
        {
            MsiPackage p => p.FullPath,
            CustomActionEntry ca => ca.FullPath,
            _ => null,
        };
        if (path != null && File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
    }

    void ExportPackages_Click(object sender, RoutedEventArgs e) =>
        ExportCsv("msi-inspector-files.csv",
            new[] { "File", "Product Name", "Version", "Manufacturer", "ProductCode", "UpgradeCode", "PackageCode", "Language", "Custom Actions", "Folder", "Problem" },
            packageView.Cast<MsiPackage>().Select(p => new[] { p.FileName, p.ProductName, p.ProductVersion, p.Manufacturer, p.ProductCode, p.UpgradeCode, p.PackageCode, p.Language, p.CustomActionCount.ToString(), p.Folder, p.Error }));

    void ExportCustomActions_Click(object sender, RoutedEventArgs e) =>
        ExportCsv("msi-inspector-custom-actions.csv",
            new[] { "Custom Action", "MSI File", "Product", "Type", "What It Is", "Source", "Target", "Path" },
            customActionView.Cast<CustomActionEntry>().Select(ca => new[] { ca.Action, ca.FileName, ca.ProductName, ca.Type.ToString(), ca.TypeDescription, ca.Source, ca.Target, ca.FullPath }));

    void ExportCsv(string defaultName, string[] header, IEnumerable<string[]> rows)
    {
        var dialog = new SaveFileDialog { FileName = defaultName, Filter = "CSV file (*.csv)|*.csv", Title = "Export what's shown" };
        if (dialog.ShowDialog(this) != true) return;
        static string Esc(string v) => v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(Esc)));
        foreach (var r in rows) sb.AppendLine(string.Join(",", r.Select(Esc)));
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true)); // BOM so Excel reads UTF-8 correctly
        StatusText.Text = $"Exported to {dialog.FileName}";
    }
}
