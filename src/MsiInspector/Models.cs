namespace MsiInspector;

/// <summary>One MSI file found during a scan.</summary>
public class MsiPackage
{
    public string FileName { get; set; } = "";
    public string Folder { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductVersion { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string UpgradeCode { get; set; } = "";
    public string PackageCode { get; set; } = "";
    public string Language { get; set; } = "";
    public int CustomActionCount { get; set; }
    public string Error { get; set; } = "";

    /// <summary>How many scanned files share the code being checked for duplicates (blank otherwise).</summary>
    public string DuplicateCount { get; set; } = "";
}

/// <summary>One row of an MSI's CustomAction table.</summary>
public class CustomActionEntry
{
    public MsiPackage Package { get; set; } = null!;
    public string Action { get; set; } = "";
    public int Type { get; set; }
    public string TypeDescription { get; set; } = "";
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";

    public string FileName => Package.FileName;
    public string ProductName => Package.ProductName;
    public string FullPath => Package.FullPath;
}

/// <summary>A custom action name found in more than one MSI.</summary>
public class SharedCustomAction
{
    public string Action { get; set; } = "";
    public int MsiCount { get; set; }
    public int Variants { get; set; }
}

/// <summary>Turns the numeric CustomAction Type into something readable.</summary>
public static class CustomActionTypes
{
    // The low 6 bits say what kind of action it is and where its code lives
    static readonly Dictionary<int, string> Kinds = new()
    {
        [1] = "DLL (stored in Binary table)",
        [2] = "EXE (stored in Binary table)",
        [5] = "JScript (stored in Binary table)",
        [6] = "VBScript (stored in Binary table)",
        [7] = "Nested install (sub-storage)",
        [17] = "DLL (installed file)",
        [18] = "EXE (installed file)",
        [19] = "Display error and abort",
        [21] = "JScript (installed file)",
        [22] = "VBScript (installed file)",
        [23] = "Nested install (source tree)",
        [34] = "EXE (path in Directory)",
        [35] = "Set directory",
        [37] = "JScript (inline)",
        [38] = "VBScript (inline)",
        [39] = "Nested install (advertised product)",
        [50] = "EXE (path in Property)",
        [51] = "Set property",
        [53] = "JScript (in Property)",
        [54] = "VBScript (in Property)",
    };

    public static string Describe(int type)
    {
        string kind = Kinds.TryGetValue(type & 0x3F, out var k) ? k : $"Type {type & 0x3F}";
        var flags = new List<string>();
        if ((type & 0x400) != 0)
        {
            flags.Add((type & 0x100) != 0 ? "rollback" : (type & 0x200) != 0 ? "commit" : "deferred");
            if ((type & 0x800) != 0) flags.Add("runs as SYSTEM");
        }
        else
        {
            flags.Add("immediate");
        }
        if ((type & 0x40) != 0) flags.Add("ignores errors");
        if ((type & 0x80) != 0) flags.Add("async");
        return $"{kind}; {string.Join(", ", flags)}";
    }
}
