namespace FBEditor.Core;

/// <summary>
/// Centralized diagnostics logger for FBEditor.
/// Replaces silent catch blocks with logged catches so errors are diagnosable.
/// Output goes to System.Diagnostics.Debug.
/// Ported from Modules/DiagnosticsLogger.vb (VB Module -> C# static class).
/// </summary>
public static class DiagnosticsLogger
{
    public static void LogWarning(string source, string message) =>
        System.Diagnostics.Debug.WriteLine($"[FBEditor][{source}] WARNING: {message}");

    public static void LogError(string source, string message, Exception? ex = null)
    {
        var msg = $"[FBEditor][{source}] ERROR: {message}";
        if (ex != null) msg += $" | {ex.GetType().Name}: {ex.Message}";
        System.Diagnostics.Debug.WriteLine(msg);
    }

    public static void LogInfo(string source, string message) =>
        System.Diagnostics.Debug.WriteLine($"[FBEditor][{source}] {message}");
}
