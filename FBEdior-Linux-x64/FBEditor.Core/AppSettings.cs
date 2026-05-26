using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace FBEditor.Core;

/// <summary>File encoding types.</summary>
public enum FileEncoding
{
    ANSI = 0,
    UTF8 = 1,
    UTF8_BOM = 2
}

/// <summary>Open file information for multi-tab support.</summary>
public class OpenFileInfo
{
    public string FilePath = "";
    public string FileName = "";
    public bool IsModified = false;
    public string Content = "";
    public int FirstVisibleLine = 0;
    public int CursorPos = 0;
    public bool IsNew = true;
    public FileEncoding FileEnc = FileEncoding.UTF8;
}

/// <summary>Parsed compiler error/warning.</summary>
public class CompilerError
{
    public string FilePath = "";
    public int LineNumber = 0;
    public string ErrorType = "";   // "error" or "warning"
    public int ErrorCode = 0;
    public string Message = "";

    public override string ToString() =>
        $"{Path.GetFileName(FilePath)}({LineNumber}) {ErrorType} {ErrorCode}: {Message}";
}

/// <summary>Build configuration settings.</summary>
public class BuildSettings
{
    public string FBCPath = "";
    public string FBC32Path = "";
    public string FBC64Path = "";
    public string FBDocPath = "";
    public string W9DocPath = "";       // Window9 documentation
    public string APIKeyFilePath = "";
    public string GDBPath = "";         // GDB debugger path
    public int TargetType = 0;          // 0=Console, 1=GUI, 2=DLL, 3=Static Lib
    public int Optimization = 0;        // 0=None, 1=O1, 2=O2, 3=O3
    public int ErrorChecking = 0;       // 0=None, 1=-e, 2=-ex, 3=-exx
    public int LangDialect = 0;         // 0=fb, 1=qb, 2=fblite, 3=deprecated
    public int CodeGen = 0;             // 0=gas, 1=gcc, 2=llvm
    public int Warnings = 0;            // 0=None, 1=All, 2=Pedantic
    public bool DebugInfo = false;
    public bool Verbose = false;
    public bool ShowCommands = false;
    public bool GenerateMap = false;
    public bool EmitASM = false;
    public bool KeepIntermediate = false;
    public int TargetArch = 0;          // 0=32bit, 1=64bit
    public int FPU = 0;                 // 0=x87, 1=sse
    public int StackSize = 0;
    public string OutputFile = "";
    public string ExtraCompilerOpts = "";
    public string ExtraLinkerOpts = "";
    public string IncludePaths = "";
    public string LibraryPaths = "";
}

/// <summary>Editor settings.</summary>
public class EditorSettings
{
    public string EditorFont = "Consolas";
    public int EditorFontSize = 11;
    public int TabWidth = 4;
    public bool UseTabs = true;
    public bool ShowLineNumbers = true;
    public bool ShowIndentGuides = true;
    public bool WordWrap = false;
    public bool ShowWhitespace = false;
    public bool AutoIndent = true;
    public bool AutoComplete = true;
    public bool HighlightCurrentLine = true;
    public bool ShowFolding = true;
    public FileEncoding DefaultEncoding = FileEncoding.UTF8;
    public bool DarkTheme = false;
    public bool SpellCheckEnabled = true;
}

/// <summary>
/// Global application settings and helpers.
/// Ported from Modules/AppSettings.vb (VB Module -> C# static class).
///
/// PLATFORM CHANGE: the VB version persisted via UserSettings (.NET Framework
/// My.Settings / app.config). That mechanism is dropped. Settings now serialize
/// straight to JSON at {SettingsPath}/settings.json — cross-platform, and on Linux
/// SpecialFolder.ApplicationData maps to ~/.config automatically.
/// </summary>
public static class AppGlobals
{
    public const string APP_NAME = "FBEditor";
    public const string APP_VERSION = "5.3.5";
    public const string APP_AUTHOR = "Ronen Blumberg";
    public const string APP_COPYRIGHT = "Copyright © 2026 Ronen Blumberg";
    public const int MAX_RECENT_FILES = 10;

    public static EditorSettings Settings = new();
    public static BuildSettings Build = new();
    public static List<string> RecentFiles = new();
    public static List<string> WatchExpressions = new();
    public static string AppPath = "";
    public static string SettingsPath = "";
    public static int NewFileCounter = 0;

    private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };

    static AppGlobals()
    {
        // Required on .NET (Core): code page 1252 is not available unless this
        // provider is registered, or Encoding.GetEncoding(1252) throws.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static string SettingsFilePath =>
        Path.Combine(string.IsNullOrEmpty(SettingsPath) ? AppPath : SettingsPath, "settings.json");

    /// <summary>Container for the JSON settings file (replaces UserSettings).</summary>
    private class SettingsData
    {
        public EditorSettings Settings = new();
        public BuildSettings Build = new();
        public List<string> RecentFiles = new();
        public List<string> WatchExpressions = new();
    }

    public static void InitializeApp()
    {
        // AppContext.BaseDirectory is cross-platform and works for single-file
        // publishes (unlike Assembly.Location). It already ends with a separator.
        AppPath = AppContext.BaseDirectory;

        try
        {
            SettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), APP_NAME);
            if (!Directory.Exists(SettingsPath)) Directory.CreateDirectory(SettingsPath);
        }
        catch
        {
            SettingsPath = AppPath;
        }

        LoadSettings();
    }

    public static void LoadSettings()
    {
        try
        {
            var f = SettingsFilePath;
            if (!File.Exists(f)) return;
            var data = JsonConvert.DeserializeObject<SettingsData>(File.ReadAllText(f));
            if (data != null)
            {
                Settings = data.Settings ?? new EditorSettings();
                Build = data.Build ?? new BuildSettings();
                RecentFiles = data.RecentFiles ?? new List<string>();
                WatchExpressions = data.WatchExpressions ?? new List<string>();
            }
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("AppGlobals", "Failed to load settings", ex);
        }
    }

    public static void SaveSettings()
    {
        try
        {
            var data = new SettingsData
            {
                Settings = Settings,
                Build = Build,
                RecentFiles = RecentFiles,
                WatchExpressions = WatchExpressions
            };
            File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        catch (Exception ex)
        {
            // Was a WinForms MessageBox in the VB version — Core stays UI-agnostic.
            DiagnosticsLogger.LogError("AppGlobals", "Failed to save settings", ex);
        }
    }

    public static void AddRecentFile(string filePath)
    {
        RecentFiles.RemoveAll(f => f.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, filePath);
        if (RecentFiles.Count > MAX_RECENT_FILES)
            RecentFiles.RemoveRange(MAX_RECENT_FILES, RecentFiles.Count - MAX_RECENT_FILES);
    }

    public static string NewUntitledName()
    {
        NewFileCounter += 1;
        if (NewFileCounter == 1) return "Untitled.bas";
        return "Untitled" + NewFileCounter + ".bas";
    }

    /// <summary>
    /// Find the FreeBASIC compiler. Platform-aware: probes typical Windows install
    /// dirs on Windows and standard /usr paths on Linux, then walks PATH.
    /// </summary>
    public static string FindFBCPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string[] searchPaths =
            {
                @"C:\FreeBASIC\", @"C:\fbc\",
                @"C:\Program Files\FreeBASIC\",
                @"C:\Program Files (x86)\FreeBASIC\",
                @"C:\fb_programming\",
                AppPath + @"fbc\", AppPath + @"FreeBASIC\"
            };
            foreach (var dir in searchPaths)
            {
                if (File.Exists(dir + "fbc32.exe")) return dir + "fbc32.exe";
                if (File.Exists(dir + "fbc.exe")) return dir + "fbc.exe";
            }
        }
        else
        {
            // Linux/Devuan: fbc typically installs to /usr/local/bin.
            string[] unixPaths = { "/usr/local/bin/fbc", "/usr/bin/fbc", "/bin/fbc" };
            foreach (var p in unixPaths)
                if (File.Exists(p)) return p;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            string fbcExe = OperatingSystem.IsWindows() ? "fbc.exe" : "fbc";
            string fbc32 = OperatingSystem.IsWindows() ? "fbc32.exe" : "fbc32";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var c32 = Path.Combine(dir, fbc32);
                if (File.Exists(c32)) return c32;
                var c = Path.Combine(dir, fbcExe);
                if (File.Exists(c)) return c;
            }
        }
        return "";
    }

    public static string GetEncodingName(FileEncoding enc) => enc switch
    {
        FileEncoding.UTF8 => "UTF-8",
        FileEncoding.UTF8_BOM => "UTF-8 BOM",
        _ => "ANSI"
    };

    /// <summary>
    /// Parse FBC compiler output for errors and warnings.
    /// Format: filename.bas(line) error num: message
    /// </summary>
    public static List<CompilerError> ParseCompilerErrors(string output, string baseDir = "")
    {
        var errors = new List<CompilerError>();
        if (string.IsNullOrEmpty(output)) return errors;

        const string pattern = @"^(.+?)\((\d+)\)\s+(error|warning)\s+(\d+):\s+(.+)$";
        foreach (var line in output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(line.Trim(), pattern, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var filePath = m.Groups[1].Value.Trim();
                // Resolve relative paths
                if (!Path.IsPathRooted(filePath) && !string.IsNullOrEmpty(baseDir))
                {
                    var fullPath = Path.Combine(baseDir, filePath);
                    if (File.Exists(fullPath)) filePath = fullPath;
                }
                errors.Add(new CompilerError
                {
                    FilePath = filePath,
                    LineNumber = int.Parse(m.Groups[2].Value),
                    ErrorType = m.Groups[3].Value.ToLower(),
                    ErrorCode = int.Parse(m.Groups[4].Value),
                    Message = m.Groups[5].Value
                });
            }
        }
        return errors;
    }

    public static FileEncoding DetectFileEncoding(string filePath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return FileEncoding.UTF8_BOM;

            int ci = 0;
            int multiByte = 0;
            while (ci < bytes.Length)
            {
                if (bytes[ci] < 0x80)
                {
                    ci += 1;
                }
                else if ((bytes[ci] & 0xE0) == 0xC0 && ci + 1 < bytes.Length && (bytes[ci + 1] & 0xC0) == 0x80)
                {
                    multiByte += 1; ci += 2;
                }
                else if ((bytes[ci] & 0xF0) == 0xE0 && ci + 2 < bytes.Length &&
                         (bytes[ci + 1] & 0xC0) == 0x80 && (bytes[ci + 2] & 0xC0) == 0x80)
                {
                    multiByte += 1; ci += 3;
                }
                else if (bytes[ci] >= 0x80)
                {
                    return FileEncoding.ANSI;
                }
                else
                {
                    ci += 1;
                }
            }
            if (multiByte > 0) return FileEncoding.UTF8;
            return Settings.DefaultEncoding;
        }
        catch
        {
            return Settings.DefaultEncoding;
        }
    }

    public static string ReadFileWithEncoding(string filePath, out FileEncoding detectedEnc)
    {
        detectedEnc = DetectFileEncoding(filePath);
        switch (detectedEnc)
        {
            case FileEncoding.UTF8:
            case FileEncoding.UTF8_BOM:
                return File.ReadAllText(filePath, Encoding.UTF8);
            default:
                return File.ReadAllText(filePath, Encoding.GetEncoding(1252));
        }
    }

    public static void WriteFileWithEncoding(string filePath, string content, FileEncoding enc)
    {
        switch (enc)
        {
            case FileEncoding.UTF8:
                File.WriteAllText(filePath, content, new UTF8Encoding(false));
                break;
            case FileEncoding.UTF8_BOM:
                File.WriteAllText(filePath, content, new UTF8Encoding(true));
                break;
            default:
                File.WriteAllText(filePath, content, Encoding.GetEncoding(1252));
                break;
        }
    }

    /// <summary>Safe Process.Start wrapper (opens URLs/files in the default handler).</summary>
    public static void SafeProcessStart(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            try { System.Diagnostics.Process.Start(url); } catch { /* ignore */ }
        }
    }
}
