using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace MsiInspector;

/// <summary>
/// Reads MSI packages through Windows Installer's own API (msi.dll), opened read-only.
/// </summary>
public static class MsiReader
{
    const uint ERROR_SUCCESS = 0;
    const uint ERROR_MORE_DATA = 234;
    const uint ERROR_NO_MORE_ITEMS = 259;
    const uint PID_REVNUMBER = 9; // the summary info field that holds the PackageCode
    const int MSICONDITION_TRUE = 1;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiOpenDatabase(string szDatabasePath, IntPtr szPersist, out IntPtr phDatabase);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiDatabaseOpenView(IntPtr hDatabase, string szQuery, out IntPtr phView);

    [DllImport("msi.dll")]
    static extern uint MsiViewExecute(IntPtr hView, IntPtr hRecord);

    [DllImport("msi.dll")]
    static extern uint MsiViewFetch(IntPtr hView, out IntPtr phRecord);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiRecordGetString(IntPtr hRecord, uint iField, StringBuilder? szValueBuf, ref uint pcchValueBuf);

    [DllImport("msi.dll")]
    static extern int MsiRecordGetInteger(IntPtr hRecord, uint iField);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern int MsiDatabaseIsTablePersistent(IntPtr hDatabase, string szTableName);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiGetSummaryInformation(IntPtr hDatabase, string? szDatabasePath, uint uiUpdateCount, out IntPtr phSummaryInfo);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern uint MsiSummaryInfoGetProperty(IntPtr hSummaryInfo, uint uiProperty, out uint puiDataType,
        out int piValue, ref FILETIME pftValue, StringBuilder? szValueBuf, ref uint pcchValueBuf);

    [DllImport("msi.dll")]
    static extern uint MsiCloseHandle(IntPtr hAny);

    /// <summary>Reads the identifiers and custom actions from one MSI file.</summary>
    public static (MsiPackage Package, List<CustomActionEntry> CustomActions) Read(string path)
    {
        var package = new MsiPackage
        {
            FullPath = path,
            FileName = Path.GetFileName(path),
            Folder = Path.GetDirectoryName(path) ?? "",
        };
        var customActions = new List<CustomActionEntry>();

        uint rc = MsiOpenDatabase(path, IntPtr.Zero /* read-only */, out IntPtr db);
        if (rc != ERROR_SUCCESS)
        {
            package.Error = $"Could not open (error {rc})";
            return (package, customActions);
        }

        try
        {
            foreach (var row in Query(db, "SELECT `Property`, `Value` FROM `Property`", 2))
            {
                string value = row[1];
                switch (row[0])
                {
                    case "ProductCode": package.ProductCode = value; break;
                    case "UpgradeCode": package.UpgradeCode = value; break;
                    case "ProductName": package.ProductName = value; break;
                    case "ProductVersion": package.ProductVersion = value; break;
                    case "Manufacturer": package.Manufacturer = value; break;
                    case "ProductLanguage": package.Language = value; break;
                }
            }

            package.PackageCode = ReadSummaryString(db, PID_REVNUMBER);

            if (MsiDatabaseIsTablePersistent(db, "CustomAction") == MSICONDITION_TRUE)
            {
                foreach (var row in Query(db, "SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`", 4, integerField: 2))
                {
                    int type = int.TryParse(row[1], out int t) ? t : 0;
                    customActions.Add(new CustomActionEntry
                    {
                        Package = package,
                        Action = row[0],
                        Type = type,
                        TypeDescription = CustomActionTypes.Describe(type),
                        Source = row[2],
                        Target = row[3],
                    });
                }
            }
            package.CustomActionCount = customActions.Count;
        }
        catch (Exception ex)
        {
            package.Error = ex.Message;
        }
        finally
        {
            MsiCloseHandle(db);
        }

        return (package, customActions);
    }

    /// <summary>Runs a query and returns every row as strings (one integer column can be read as a number).</summary>
    static IEnumerable<string[]> Query(IntPtr db, string sql, int fields, int integerField = -1)
    {
        uint rc = MsiDatabaseOpenView(db, sql, out IntPtr view);
        if (rc != ERROR_SUCCESS) throw new InvalidOperationException($"Query failed (error {rc}): {sql}");
        try
        {
            rc = MsiViewExecute(view, IntPtr.Zero);
            if (rc != ERROR_SUCCESS) throw new InvalidOperationException($"Query failed (error {rc}): {sql}");

            var rows = new List<string[]>();
            while (MsiViewFetch(view, out IntPtr record) == ERROR_SUCCESS)
            {
                try
                {
                    var values = new string[fields];
                    for (uint i = 1; i <= fields; i++)
                    {
                        values[i - 1] = i == integerField
                            ? MsiRecordGetInteger(record, i).ToString()
                            : GetString(record, i);
                    }
                    rows.Add(values);
                }
                finally
                {
                    MsiCloseHandle(record);
                }
            }
            return rows;
        }
        finally
        {
            MsiCloseHandle(view);
        }
    }

    static string GetString(IntPtr record, uint field)
    {
        uint size = 256;
        var buffer = new StringBuilder((int)size);
        uint rc = MsiRecordGetString(record, field, buffer, ref size);
        if (rc == ERROR_MORE_DATA)
        {
            size++;
            buffer = new StringBuilder((int)size);
            rc = MsiRecordGetString(record, field, buffer, ref size);
        }
        return rc == ERROR_SUCCESS ? buffer.ToString() : "";
    }

    static string ReadSummaryString(IntPtr db, uint property)
    {
        if (MsiGetSummaryInformation(db, null, 0, out IntPtr summary) != ERROR_SUCCESS) return "";
        try
        {
            var time = new FILETIME();
            uint size = 256;
            var buffer = new StringBuilder((int)size);
            uint rc = MsiSummaryInfoGetProperty(summary, property, out _, out _, ref time, buffer, ref size);
            if (rc == ERROR_MORE_DATA)
            {
                size++;
                buffer = new StringBuilder((int)size);
                rc = MsiSummaryInfoGetProperty(summary, property, out _, out _, ref time, buffer, ref size);
            }
            return rc == ERROR_SUCCESS ? buffer.ToString() : "";
        }
        finally
        {
            MsiCloseHandle(summary);
        }
    }
}
