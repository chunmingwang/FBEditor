using System.Diagnostics;
using System.IO;
using System.Text;
using static FBEditor.Core.AppGlobals; // brings Build, AppPath, APP_NAME into scope (as in the VB module)

namespace FBEditor.Core;

public class BuildResult
{
    public bool Success = false;
    public int ExitCode = -1;
    public string Output = "";
    public string CommandLine = "";
    public double Duration = 0;
}

/// <summary>
/// Invokes the FreeBASIC compiler and captures its output.
/// Ported from Modules/BuildSystem.vb (VB Module -> C# static class).
/// Platform-adapted for Linux: no .exe output extension, no cmd.exe, no WinForms.
/// </summary>
public static class BuildSystem
{
    public static BuildResult BuildFile(string sourceFile, bool runAfter = false, bool syntaxOnly = false)
    {
        var result = new BuildResult();

        // Validate FBC path
        var fbcPath = GetActiveFBCPath();
        if (string.IsNullOrEmpty(fbcPath))
        {
            result.Output = "ERROR: FreeBASIC compiler path not set." + Environment.NewLine +
                            "Please configure the compiler path in Build > Build Options...";
            return result;
        }
        if (!File.Exists(fbcPath))
        {
            result.Output = "ERROR: FreeBASIC compiler not found at: " + fbcPath;
            return result;
        }

        // Build command line
        var args = BuildCommandLine(sourceFile, syntaxOnly);
        result.CommandLine = $"\"{fbcPath}\" {args}";

        // Execute compiler
        var sw = Stopwatch.StartNew();
        result.Output = ExecuteProcess(fbcPath, args, Path.GetDirectoryName(sourceFile) ?? "", ref result.ExitCode);
        sw.Stop();
        result.Duration = sw.Elapsed.TotalSeconds;
        result.Success = result.ExitCode == 0;

        // Header
        var header = new StringBuilder();
        header.AppendLine("Compiler: " + fbcPath);
        header.AppendLine("Command:  " + result.CommandLine);
        header.AppendLine("Source:   " + sourceFile);
        header.AppendLine(new string('-', 60));
        result.Output = header.ToString() + result.Output;

        // Summary
        if (result.Success)
        {
            result.Output += Environment.NewLine + new string('-', 60) + Environment.NewLine +
                             $"Build successful! ({result.Duration:F2}s)";
            if (runAfter && !syntaxOnly)
            {
                var exePath = GetOutputExePath(sourceFile);
                if (File.Exists(exePath))
                {
                    result.Output += Environment.NewLine + "Running: " + exePath;
                    RunExecutable(exePath, Path.GetDirectoryName(sourceFile));
                }
                else
                {
                    result.Output += Environment.NewLine + "WARNING: Output not found: " + exePath;
                }
            }
        }
        else
        {
            result.Output += Environment.NewLine + new string('-', 60) + Environment.NewLine +
                             $"Build FAILED with exit code {result.ExitCode} ({result.Duration:F2}s)";
        }

        return result;
    }

    public static string GetActiveFBCPath()
    {
        // Use primary path, or fall back to 32/64 based on target
        if (!string.IsNullOrEmpty(Build.FBCPath)) return Build.FBCPath;
        if (Build.TargetArch == 0 && !string.IsNullOrEmpty(Build.FBC32Path)) return Build.FBC32Path;
        if (Build.TargetArch == 1 && !string.IsNullOrEmpty(Build.FBC64Path)) return Build.FBC64Path;
        if (!string.IsNullOrEmpty(Build.FBC32Path)) return Build.FBC32Path;
        if (!string.IsNullOrEmpty(Build.FBC64Path)) return Build.FBC64Path;
        return "";
    }

    public static string BuildCommandLine(string sourceFile, bool syntaxOnly = false)
    {
        var sb = new StringBuilder();

        if (syntaxOnly)
        {
            sb.Append($" -pp \"{sourceFile}\"");
            return sb.ToString();
        }

        // Target type
        switch (Build.TargetType)
        {
            case 1: sb.Append(" -s gui"); break;
            case 2: sb.Append(" -dll"); break;
            case 3: sb.Append(" -lib"); break;
        }

        // Dialect
        switch (Build.LangDialect)
        {
            case 1: sb.Append(" -lang qb"); break;
            case 2: sb.Append(" -lang fblite"); break;
            case 3: sb.Append(" -lang deprecated"); break;
        }

        // Optimization
        if (Build.Optimization > 0) sb.Append($" -O {Build.Optimization}");

        // Error checking
        switch (Build.ErrorChecking)
        {
            case 1: sb.Append(" -e"); break;
            case 2: sb.Append(" -ex"); break;
            case 3: sb.Append(" -exx"); break;
        }

        // Code generator
        switch (Build.CodeGen)
        {
            case 1: sb.Append(" -gen gcc"); break;
            case 2: sb.Append(" -gen llvm"); break;
        }

        // Warnings
        switch (Build.Warnings)
        {
            case 1: sb.Append(" -w all"); break;
            case 2: sb.Append(" -w pedantic"); break;
        }

        // Architecture
        if (Build.TargetArch == 1) sb.Append(" -arch x86_64");

        // FPU
        if (Build.FPU == 1) sb.Append(" -fpu sse");

        // Boolean flags
        if (Build.DebugInfo) sb.Append(" -g");
        if (Build.Verbose) sb.Append(" -v");
        if (Build.ShowCommands) sb.Append(" -showincludes");
        if (Build.GenerateMap) sb.Append(" -map");
        if (Build.EmitASM) sb.Append(" -R");
        if (Build.KeepIntermediate) sb.Append(" -C");

        // Stack size
        if (Build.StackSize > 0) sb.Append($" -t {Build.StackSize}");

        // Include paths
        if (!string.IsNullOrEmpty(Build.IncludePaths))
            foreach (var p in Build.IncludePaths.Split(';'))
                if (p.Trim() != "") sb.Append($" -i \"{p.Trim()}\"");

        // Library paths
        if (!string.IsNullOrEmpty(Build.LibraryPaths))
            foreach (var p in Build.LibraryPaths.Split(';'))
                if (p.Trim() != "") sb.Append($" -p \"{p.Trim()}\"");

        // Output
        if (!string.IsNullOrEmpty(Build.OutputFile)) sb.Append($" -x \"{Build.OutputFile}\"");

        // Extra options
        if (!string.IsNullOrEmpty(Build.ExtraCompilerOpts?.Trim())) sb.Append(" " + Build.ExtraCompilerOpts.Trim());
        if (!string.IsNullOrEmpty(Build.ExtraLinkerOpts?.Trim())) sb.Append($" -Wl \"{Build.ExtraLinkerOpts.Trim()}\"");

        // Window9 project flags (v5.2.0)
        if (ProjectManager.IsWindow9Project)
        {
            var w9Flags = ProjectManager.GetProjectCompilerFlags();
            if (!string.IsNullOrEmpty(w9Flags))
            {
                // Only add -s gui if not already set by TargetType
                if (Build.TargetType != 1 && w9Flags.Contains("-s gui"))
                    sb.Append(" -s gui");
                // Add Window9 include/lib paths
                var proj = ProjectManager.CurrentProject;
                if (!string.IsNullOrEmpty(proj.Window9IncludePath))
                    sb.Append($" -i \"{proj.Window9IncludePath}\"");
                if (!string.IsNullOrEmpty(proj.Window9LibPath))
                    sb.Append($" -p \"{proj.Window9LibPath}\"");
            }
        }

        // Source file last
        sb.Append($" \"{sourceFile}\"");
        return sb.ToString();
    }

    public static string GetOutputExePath(string sourceFile)
    {
        if (!string.IsNullOrEmpty(Build.OutputFile)) return Build.OutputFile;
        var baseName = Path.ChangeExtension(sourceFile, null);
        // Linux: console/GUI executables have NO extension; DLL -> .so.
        switch (Build.TargetType)
        {
            case 2: return baseName + (OperatingSystem.IsWindows() ? ".dll" : ".so");
            case 3: return baseName + ".a";
            default: return OperatingSystem.IsWindows() ? baseName + ".exe" : baseName!;
        }
    }

    public static string ExecuteProcess(string exePath, string arguments, string workDir, ref int exitCode)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(workDir) ? AppPath : workDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var output = new StringBuilder();
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                exitCode = -1;
                return "ERROR: failed to start compiler process";
            }

            proc.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            if (proc.WaitForExit(60000))
            {
                proc.WaitForExit(); // Ensure streams flushed
                exitCode = proc.ExitCode;
            }
            else
            {
                proc.Kill();
                exitCode = -99;
                output.AppendLine("*** BUILD TIMED OUT after 60 seconds ***");
            }
            return output.ToString();
        }
        catch (Exception ex)
        {
            exitCode = -1;
            return "ERROR: " + ex.Message;
        }
    }

    public static void RunExecutable(string exePath, string? workDir)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"{exePath}\"",
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                });
                return;
            }

            // Linux: try to launch in a terminal so console output is visible.
            // GUI/Window9 apps don't need it, but console programs do.
            string[] terminals = { "x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "xterm" };
            foreach (var term in terminals)
            {
                try
                {
                    var psi = new ProcessStartInfo { FileName = term, WorkingDirectory = workDir, UseShellExecute = false };
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(exePath);
                    Process.Start(psi);
                    return;
                }
                catch { /* try next terminal */ }
            }

            // No terminal emulator found — run the program directly.
            Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = workDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // Was a WinForms MessageBox in the VB version.
            DiagnosticsLogger.LogError("BuildSystem", $"Failed to run: {exePath}", ex);
        }
    }

    public static BuildResult QuickRun(string sourceFile)
    {
        var origOutput = Build.OutputFile;
        // Linux temp executable has no .exe extension.
        var tempName = OperatingSystem.IsWindows() ? "rbfbide_quickrun.exe" : "rbfbide_quickrun";
        Build.OutputFile = Path.Combine(Path.GetTempPath(), tempName);
        var result = BuildFile(sourceFile, true);
        Build.OutputFile = origOutput;
        return result;
    }
}
