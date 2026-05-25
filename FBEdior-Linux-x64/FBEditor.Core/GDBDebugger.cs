using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FBEditor.Core;

/// <summary>
/// GDB debugger integration using GDB/MI (Machine Interface) protocol.
/// Breakpoints, stepping, variable inspection, call stack navigation.
/// Ported from Modules/GDBDebugger.vb.
///
/// GDB/MI is identical across platforms; the platform-specific bits are gdb
/// discovery (FindGDBPath) and the Windows-only "set new-console" command.
/// </summary>
public class GDBDebugger : IDisposable
{
    // ---- Events (all fire on the captured SynchronizationContext) ----
    public event Action? DebugStarted;
    public event Action? DebugStopped;
    public event Action<string, int>? DebugPaused;       // (filePath, lineNumber)
    public event Action? DebugResumed;
    public event Action<string>? DebugOutput;
    public event Action<string>? DebugError;
    public event Action<List<VariableInfo>>? LocalsUpdated;
    public event Action<List<VariableInfo>>? WatchUpdated;
    public event Action<List<StackFrameInfo>>? CallStackUpdated;
    public event Action<int, string, int>? BreakpointHit; // (bpNumber, filePath, lineNumber)

    // ---- State ----
    private Process? _process;
    private readonly SynchronizationContext _syncCtx;
    private bool _isRunning;
    private bool _isPaused;
    private bool _disposed;
    private string _gdbPath = "";
    private string _sourceFile = "";
    private string _exePath = "";
    private string _workDir = "";
    private int _miTokenCounter;

    // ---- Breakpoints ----
    private readonly Dictionary<string, List<BreakpointInfo>> _breakpoints = new();
    private readonly List<string> _watchExpressions = new();
    private readonly Dictionary<int, string> _pendingWatchTokens = new(); // token -> expression
    private readonly List<VariableInfo> _watchResults = new();
    private int _watchResultCount;
    private string _currentFile = "";
    private int _currentLine;

    // ---- Locals collection (text-based info locals/args for FreeBASIC compatibility) ----
    private int _localsToken = -1;
    private int _argsToken = -1;
    private readonly List<string> _localsLines = new();
    private readonly List<string> _argsLines = new();
    private bool _localsCollected;
    private bool _argsCollected;
    private readonly object _localsLock = new(); // synchronizes token assignment across threads

    // ---- Data classes ----
    public class BreakpointInfo
    {
        public int Number = 0;
        public string FilePath = "";
        public int LineNumber = 0;
        public bool Enabled = true;
        public string Condition = "";
        public int HitCount = 0;
        public bool Pending = true;
    }

    public class VariableInfo
    {
        public string Name = "";
        public string Value = "";
        public string DataType = "";
    }

    public class StackFrameInfo
    {
        public int Level = 0;
        public string FunctionName = "";
        public string FilePath = "";
        public int LineNumber = 0;
        public string Address = "";
    }

    // ---- Properties ----
    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public string CurrentFile => _currentFile;
    public int CurrentLine => _currentLine;
    public Dictionary<string, List<BreakpointInfo>> Breakpoints => _breakpoints;
    public List<string> WatchExpressions => _watchExpressions;

    public GDBDebugger()
    {
        _syncCtx = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    // ---- GDB Path Detection ----
    public static string FindGDBPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string[] searchPaths =
            {
                @"C:\MinGW\bin\gdb.exe",
                @"C:\msys64\mingw64\bin\gdb.exe",
                @"C:\msys64\mingw32\bin\gdb.exe",
                @"C:\TDM-GCC-64\bin\gdb.exe",
                @"C:\TDM-GCC-32\bin\gdb.exe",
                @"C:\Program Files\MinGW\bin\gdb.exe",
                @"C:\Program Files (x86)\MinGW\bin\gdb.exe"
            };
            foreach (var p in searchPaths)
                if (File.Exists(p)) return p;
        }
        else
        {
            string[] unixPaths = { "/usr/bin/gdb", "/usr/local/bin/gdb", "/bin/gdb" };
            foreach (var p in unixPaths)
                if (File.Exists(p)) return p;
        }

        string gdbName = OperatingSystem.IsWindows() ? "gdb.exe" : "gdb";

        // Check near the FreeBASIC compiler
        if (!string.IsNullOrEmpty(AppGlobals.Build.FBCPath))
        {
            var fbcDir = Path.GetDirectoryName(AppGlobals.Build.FBCPath);
            if (fbcDir != null)
            {
                var gdb1 = Path.Combine(fbcDir, gdbName);
                if (File.Exists(gdb1)) return gdb1;
                var parentDir = Path.GetDirectoryName(fbcDir);
                if (parentDir != null)
                {
                    var gdb2 = Path.Combine(parentDir, "bin", gdbName);
                    if (File.Exists(gdb2)) return gdb2;
                }
            }
        }

        // Search PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var d in pathEnv.Split(Path.PathSeparator))
            {
                var p = Path.Combine(d.Trim(), gdbName);
                if (File.Exists(p)) return p;
            }
        }
        return "";
    }

    // ---- Start / Stop ----
    public bool StartDebugging(string gdbPath, string exePath, string sourceFile, string workDir)
    {
        if (_isRunning)
        {
            FireOnUI(() => DebugError?.Invoke("Debugger is already running."));
            return false;
        }
        if (string.IsNullOrEmpty(gdbPath) || !File.Exists(gdbPath))
        {
            FireOnUI(() => DebugError?.Invoke("GDB not found: " + (string.IsNullOrEmpty(gdbPath) ? "(empty)" : gdbPath)));
            return false;
        }
        if (!File.Exists(exePath))
        {
            FireOnUI(() => DebugError?.Invoke("Executable not found: " + exePath));
            return false;
        }

        _gdbPath = gdbPath;
        _exePath = exePath;
        _sourceFile = sourceFile;
        _workDir = string.IsNullOrEmpty(workDir) ? (Path.GetDirectoryName(exePath) ?? "") : workDir;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = gdbPath,
                Arguments = $"--interpreter=mi2 \"{exePath}\"",
                WorkingDirectory = _workDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = new Process { StartInfo = psi };
            _process.Start();

            var outThread = new Thread(ReadGDBOutput) { IsBackground = true, Name = "GDB_Out" };
            outThread.Start();

            var errThread = new Thread(() =>
            {
                try
                {
                    while (!_disposed && _process != null && !_process.HasExited)
                    {
                        var line = _process.StandardError.ReadLine();
                        if (line != null) FireOnUI(() => DebugOutput?.Invoke("[GDB ERR] " + line));
                    }
                }
                catch (Exception ex) { DiagnosticsLogger.LogError("GDBDebugger", "Stderr reader error", ex); }
            }) { IsBackground = true, Name = "GDB_Err" };
            errThread.Start();

            _isRunning = true;
            _isPaused = false;

            // "set new-console" is a Windows-only gdb setting; skip it on Linux.
            if (OperatingSystem.IsWindows()) SendCmd("-gdb-set new-console on");
            SendCmd("-gdb-set print pretty on");
            SendCmd("-gdb-set pagination off");
            SendAllBreakpoints();

            FireOnUI(() =>
            {
                DebugStarted?.Invoke();
                DebugOutput?.Invoke("Debugger started: " + exePath);
            });
            return true;
        }
        catch (Exception ex)
        {
            FireOnUI(() => DebugError?.Invoke("Failed to start GDB: " + ex.Message));
            return false;
        }
    }

    public void StopDebugging()
    {
        if (!_isRunning) return;
        try
        {
            if (_process != null && !_process.HasExited)
            {
                SendCmdDirect("-gdb-exit");
                if (!_process.WaitForExit(2000)) _process.Kill();
            }
        }
        catch (Exception ex) { DiagnosticsLogger.LogError("GDBDebugger", "StopDebugging cleanup error", ex); }
        CleanupProcess();
        _isRunning = false; _isPaused = false; _currentFile = ""; _currentLine = 0;
        FireOnUI(() =>
        {
            DebugStopped?.Invoke();
            DebugOutput?.Invoke("Debugger stopped.");
        });
    }

    // ---- Execution Control ----
    public void Run()
    {
        if (!_isRunning) return;
        SendCmd("-exec-run"); _isPaused = false;
        FireOnUI(() => DebugResumed?.Invoke());
    }

    public void Continue()
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd("-exec-continue"); _isPaused = false;
        FireOnUI(() => DebugResumed?.Invoke());
    }

    public void Pause()
    {
        if (!_isRunning || _isPaused) return;
        SendCmd("-exec-interrupt");
    }

    public void StepOver()
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd("-exec-next"); _isPaused = false;
        FireOnUI(() => DebugResumed?.Invoke());
    }

    public void StepInto()
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd("-exec-step"); _isPaused = false;
        FireOnUI(() => DebugResumed?.Invoke());
    }

    public void StepOut()
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd("-exec-finish"); _isPaused = false;
        FireOnUI(() => DebugResumed?.Invoke());
    }

    public void RunToCursor(string filePath, int lineNumber)
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd($"-exec-until \"{Norm(filePath)}:{lineNumber}\""); _isPaused = false;
        FireOnUI(() => DebugResumed?.Invoke());
    }

    // ---- Breakpoint Management ----
    public BreakpointInfo AddBreakpoint(string filePath, int lineNumber)
    {
        var key = Norm(filePath).ToLowerInvariant();
        if (!_breakpoints.ContainsKey(key)) _breakpoints[key] = new List<BreakpointInfo>();
        foreach (var bp in _breakpoints[key])
            if (bp.LineNumber == lineNumber) return bp;
        var newBP = new BreakpointInfo { FilePath = filePath, LineNumber = lineNumber, Enabled = true, Pending = true };
        _breakpoints[key].Add(newBP);
        if (_isRunning)
        {
            SendCmd($"-break-insert \"{Norm(filePath)}:{lineNumber}\"");
            newBP.Pending = false;
        }
        return newBP;
    }

    public void RemoveBreakpoint(string filePath, int lineNumber)
    {
        var key = Norm(filePath).ToLowerInvariant();
        if (!_breakpoints.ContainsKey(key)) return;
        BreakpointInfo? toRemove = null;
        foreach (var bp in _breakpoints[key])
            if (bp.LineNumber == lineNumber) { toRemove = bp; break; }
        if (toRemove != null)
        {
            if (_isRunning && toRemove.Number > 0) SendCmd($"-break-delete {toRemove.Number}");
            _breakpoints[key].Remove(toRemove);
        }
    }

    public bool ToggleBreakpoint(string filePath, int lineNumber)
    {
        var key = Norm(filePath).ToLowerInvariant();
        if (_breakpoints.ContainsKey(key))
        {
            foreach (var bp in _breakpoints[key])
            {
                if (bp.LineNumber == lineNumber)
                {
                    RemoveBreakpoint(filePath, lineNumber);
                    return false;
                }
            }
        }
        AddBreakpoint(filePath, lineNumber);
        return true;
    }

    public bool HasBreakpoint(string filePath, int lineNumber)
    {
        var key = Norm(filePath).ToLowerInvariant();
        if (!_breakpoints.ContainsKey(key)) return false;
        foreach (var bp in _breakpoints[key])
            if (bp.LineNumber == lineNumber) return true;
        return false;
    }

    public List<BreakpointInfo> GetBreakpointsForFile(string filePath)
    {
        var key = Norm(filePath).ToLowerInvariant();
        if (_breakpoints.ContainsKey(key)) return _breakpoints[key];
        return new List<BreakpointInfo>();
    }

    public void ClearAllBreakpoints()
    {
        if (_isRunning) SendCmd("-break-delete");
        _breakpoints.Clear();
    }

    // ---- Watch / Inspect ----
    public void AddWatch(string expression)
    {
        if (!_watchExpressions.Contains(expression))
        {
            _watchExpressions.Add(expression);
            if (_isRunning && _isPaused) RefreshWatches();
        }
    }

    public void RemoveWatch(string expression) => _watchExpressions.Remove(expression);

    public void RefreshWatches()
    {
        if (!_isRunning || !_isPaused) return;
        if (_watchExpressions.Count == 0)
        {
            FireOnUI(() => WatchUpdated?.Invoke(new List<VariableInfo>()));
            return;
        }
        lock (_pendingWatchTokens)
        {
            _pendingWatchTokens.Clear();
            _watchResults.Clear();
            _watchResultCount = _watchExpressions.Count;
        }
        foreach (var expr in _watchExpressions)
        {
            var token = SendCmd($"-data-evaluate-expression \"{EscGDB(expr)}\"");
            if (token > 0)
            {
                lock (_pendingWatchTokens)
                    _pendingWatchTokens[token] = expr;
            }
        }
    }

    public void RequestLocals()
    {
        if (!_isRunning || !_isPaused) return;
        // Text-based "info locals" / "info args" instead of MI -stack-list-locals:
        // FreeBASIC debug info works much better with the text commands.
        lock (_localsLock)
        {
            lock (_localsLines)
            {
                _localsLines.Clear();
                _argsLines.Clear();
                _localsCollected = false;
                _argsCollected = false;
            }
            _localsToken = SendCmd("-interpreter-exec console \"info locals\"");
            _argsToken = SendCmd("-interpreter-exec console \"info args\"");
        }
    }

    public void RequestCallStack()
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd("-stack-list-frames");
    }

    public void SelectFrame(int level)
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd($"-stack-select-frame {level}");
        RequestLocals();
    }

    public void EvaluateExpression(string expression)
    {
        if (!_isRunning || !_isPaused) return;
        SendCmd($"-data-evaluate-expression \"{EscGDB(expression)}\"");
    }

    public void SendRawCommand(string command)
    {
        if (!_isRunning) return;
        SendCmd(command);
    }

    // ---- GDB Communication ----
    private int SendCmd(string command)
    {
        if (_process == null || _process.HasExited) return -1;
        try
        {
            _miTokenCounter += 1;
            var token = _miTokenCounter;
            var fullCmd = $"{token}{command}";
            _process.StandardInput.WriteLine(fullCmd);
            _process.StandardInput.Flush();
            return token;
        }
        catch (Exception ex)
        {
            DiagnosticsLogger.LogError("GDBDebugger", $"SendCmd failed: {command}", ex);
            return -1;
        }
    }

    private void SendCmdDirect(string command)
    {
        if (_process == null || _process.HasExited) return;
        try
        {
            _process.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
        }
        catch (Exception ex) { DiagnosticsLogger.LogError("GDBDebugger", "SendCmdDirect failed", ex); }
    }

    private void SendAllBreakpoints()
    {
        foreach (var kvp in _breakpoints)
            foreach (var bp in kvp.Value)
                if (bp.Pending || bp.Number == 0)
                {
                    SendCmd($"-break-insert \"{Norm(bp.FilePath)}:{bp.LineNumber}\"");
                    bp.Pending = false;
                }
    }

    // ---- GDB Output Reader ----
    private void ReadGDBOutput()
    {
        try
        {
            while (!_disposed && _process != null && !_process.HasExited)
            {
                var line = _process.StandardOutput.ReadLine();
                if (line == null) break;
                ProcessLine(line);
            }
        }
        catch (Exception ex) { DiagnosticsLogger.LogError("GDBDebugger", "GDB output reader error", ex); }
        if (_isRunning)
        {
            _isRunning = false; _isPaused = false;
            FireOnUI(() => DebugStopped?.Invoke());
        }
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        var c = line[0];
        switch (c)
        {
            case '~':
                var text = ExtractQuoted(line, 1);
                // Check if this console output belongs to a pending locals/args request
                bool captured = false;
                lock (_localsLines)
                {
                    if (_localsToken > 0 && !_localsCollected)
                    {
                        _localsLines.Add(text);
                        captured = true;
                    }
                    else if (_argsToken > 0 && !_argsCollected && _localsCollected)
                    {
                        _argsLines.Add(text);
                        captured = true;
                    }
                }
                if (!captured) FireOnUI(() => DebugOutput?.Invoke(text));
                break;
            case '@':
                FireOnUI(() => DebugOutput?.Invoke("[TARGET] " + ExtractQuoted(line, 1)));
                break;
            case '&':
                break; // Suppress noisy log stream
            case '*':
                ParseAsync(line.Substring(1));
                break;
            case '=':
                ParseNotify(line.Substring(1));
                break;
            case '^':
                ParseResult(line, 0);
                break;
            default:
                // Extract token number from prefix
                var tokenStr = "";
                var idx = 0;
                while (idx < line.Length && char.IsDigit(line[idx]))
                {
                    tokenStr += line[idx];
                    idx += 1;
                }
                int token = 0;
                int.TryParse(tokenStr, out token);
                if (idx < line.Length)
                {
                    switch (line[idx])
                    {
                        case '^': ParseResult(line.Substring(idx), token); break;
                        case '*': ParseAsync(line.Substring(idx + 1)); break;
                        case '=': ParseNotify(line.Substring(idx + 1)); break;
                    }
                }
                break;
        }
    }

    private void ParseAsync(string data)
    {
        if (data.StartsWith("stopped"))
        {
            _isPaused = true;
            var reason = GetField(data, "reason");
            var fp = GetField(data, "fullname");
            if (string.IsNullOrEmpty(fp)) fp = GetField(data, "file");
            int ln = 0; int.TryParse(GetField(data, "line"), out ln);
            _currentFile = string.IsNullOrEmpty(fp) ? _sourceFile : fp;
            _currentLine = ln;

            switch (reason)
            {
                case "breakpoint-hit":
                    int bpn = 0; int.TryParse(GetField(data, "bkptno"), out bpn);
                    FireOnUI(() =>
                    {
                        DebugPaused?.Invoke(_currentFile, _currentLine);
                        BreakpointHit?.Invoke(bpn, _currentFile, _currentLine);
                        DebugOutput?.Invoke($"Breakpoint {bpn} hit at {Path.GetFileName(_currentFile)}:{_currentLine}");
                    });
                    break;
                case "end-stepping-range":
                case "function-finished":
                    FireOnUI(() =>
                    {
                        DebugPaused?.Invoke(_currentFile, _currentLine);
                        DebugOutput?.Invoke($"Stopped at {Path.GetFileName(_currentFile)}:{_currentLine}");
                    });
                    break;
                case "signal-received":
                    var sn = GetField(data, "signal-name");
                    var sm = GetField(data, "signal-meaning");
                    FireOnUI(() =>
                    {
                        DebugPaused?.Invoke(_currentFile, _currentLine);
                        DebugError?.Invoke($"Signal: {sn} - {sm}");
                    });
                    break;
                case "exited-normally":
                    _isRunning = false; _isPaused = false;
                    FireOnUI(() =>
                    {
                        DebugOutput?.Invoke("Program exited normally.");
                        DebugStopped?.Invoke();
                    });
                    return;
                case "exited":
                    var ec = GetField(data, "exit-code");
                    _isRunning = false; _isPaused = false;
                    FireOnUI(() =>
                    {
                        DebugOutput?.Invoke($"Program exited with code {ec}.");
                        DebugStopped?.Invoke();
                    });
                    return;
                case "exited-signalled":
                    var sn2 = GetField(data, "signal-name");
                    _isRunning = false; _isPaused = false;
                    FireOnUI(() =>
                    {
                        DebugError?.Invoke($"Program terminated by signal: {sn2}");
                        DebugStopped?.Invoke();
                    });
                    return;
                default:
                    FireOnUI(() =>
                    {
                        DebugPaused?.Invoke(_currentFile, _currentLine);
                        DebugOutput?.Invoke($"Stopped ({reason}) at {Path.GetFileName(_currentFile)}:{_currentLine}");
                    });
                    break;
            }
            if (_isPaused)
            {
                RequestLocals();
                RequestCallStack();
                if (_watchExpressions.Count > 0) RefreshWatches();
            }
        }
        else if (data.StartsWith("running"))
        {
            _isPaused = false;
            FireOnUI(() => DebugResumed?.Invoke());
        }
    }

    private void ParseNotify(string data)
    {
        if (data.StartsWith("breakpoint-created") || data.StartsWith("breakpoint-modified"))
        {
            int bpn = 0; int.TryParse(GetField(data, "number"), out bpn);
            var fp = GetField(data, "fullname");
            if (string.IsNullOrEmpty(fp)) fp = GetField(data, "file");
            int ln = 0; int.TryParse(GetField(data, "line"), out ln);
            if (bpn > 0) UpdateBPNumber(fp, ln, bpn);
        }
    }

    private void ParseResult(string data, int token)
    {
        var s = data.TrimStart('^');
        if (s.StartsWith("done"))
        {
            // Check if this is a locals/args completion (synchronized with RequestLocals)
            bool isLocals, isArgs;
            lock (_localsLock)
            {
                isLocals = token > 0 && token == _localsToken;
                isArgs = token > 0 && token == _argsToken;
            }

            if (isLocals)
            {
                lock (_localsLines) _localsCollected = true;
                lock (_localsLock) _localsToken = -1;
                if (_argsToken <= 0 || _argsCollected) FireLocalsFromTextOutput();
                return;
            }
            else if (isArgs)
            {
                lock (_localsLines) _argsCollected = true;
                lock (_localsLock) _argsToken = -1;
                FireLocalsFromTextOutput();
                return;
            }

            // Check if this is a watch result
            bool isWatch = false;
            var watchExpr = "";
            lock (_pendingWatchTokens)
            {
                if (token > 0 && _pendingWatchTokens.ContainsKey(token))
                {
                    isWatch = true;
                    watchExpr = _pendingWatchTokens[token];
                    _pendingWatchTokens.Remove(token);
                }
            }

            if (isWatch)
            {
                var v = ExtractMIValue(s, "value");
                lock (_pendingWatchTokens)
                {
                    _watchResults.Add(new VariableInfo { Name = watchExpr, Value = string.IsNullOrEmpty(v) ? "(unknown)" : v });
                    if (_watchResults.Count >= _watchResultCount || _pendingWatchTokens.Count == 0)
                    {
                        var results = new List<VariableInfo>(_watchResults);
                        FireOnUI(() => WatchUpdated?.Invoke(results));
                    }
                }
            }
            else if (s.Contains("stack="))
            {
                ParseStack(s);
            }
            else if (s.Contains("value="))
            {
                var v = ExtractMIValue(s, "value");
                FireOnUI(() => DebugOutput?.Invoke("[EVAL] " + v));
            }
            if (s.Contains("bkpt="))
            {
                int bpn = 0; int.TryParse(GetField(s, "number"), out bpn);
                var fp = GetField(s, "fullname");
                if (string.IsNullOrEmpty(fp)) fp = GetField(s, "file");
                int ln = 0; int.TryParse(GetField(s, "line"), out ln);
                if (bpn > 0) UpdateBPNumber(fp, ln, bpn);
            }
        }
        else if (s.StartsWith("error"))
        {
            // Locals/args error (still mark collected so the locals event fires)
            if (token > 0 && token == _localsToken)
            {
                lock (_localsLines) _localsCollected = true;
                _localsToken = -1;
                if (_argsToken <= 0 || _argsCollected) FireLocalsFromTextOutput();
                return;
            }
            else if (token > 0 && token == _argsToken)
            {
                lock (_localsLines) _argsCollected = true;
                _argsToken = -1;
                FireLocalsFromTextOutput();
                return;
            }
            // Watch error
            bool isWatch = false;
            var watchExpr = "";
            lock (_pendingWatchTokens)
            {
                if (token > 0 && _pendingWatchTokens.ContainsKey(token))
                {
                    isWatch = true;
                    watchExpr = _pendingWatchTokens[token];
                    _pendingWatchTokens.Remove(token);
                }
            }
            var msg = GetField(s, "msg");
            if (isWatch)
            {
                lock (_pendingWatchTokens)
                {
                    _watchResults.Add(new VariableInfo { Name = watchExpr, Value = "<error: " + msg + ">" });
                    if (_watchResults.Count >= _watchResultCount || _pendingWatchTokens.Count == 0)
                    {
                        var results = new List<VariableInfo>(_watchResults);
                        FireOnUI(() => WatchUpdated?.Invoke(results));
                    }
                }
            }
            else
            {
                FireOnUI(() => DebugError?.Invoke("[GDB Error] " + msg));
            }
        }
        else if (s.StartsWith("running"))
        {
            _isPaused = false;
        }
    }

    /// <summary>Parse collected text output from "info locals"/"info args" and fire LocalsUpdated.</summary>
    private void FireLocalsFromTextOutput()
    {
        var locals = new List<VariableInfo>();
        var allLines = new List<string>();
        lock (_localsLines)
        {
            allLines.AddRange(_argsLines);   // args first (function parameters)
            allLines.AddRange(_localsLines); // then locals
        }

        // GDB output format:  varname = value  |  arr = {1, 2, 3}  |  s = 0x123 "hello"
        var currentName = "";
        var currentValue = "";

        foreach (var rawLine in allLines)
        {
            var ln = rawLine.TrimEnd('\n', '\r', ' ');
            if (string.IsNullOrEmpty(ln)) continue;
            if (ln == "No locals." || ln == "No arguments.") continue;

            var eqIdx = ln.IndexOf(" = ");
            bool startsNewVar = false;
            if (eqIdx > 0)
            {
                var namePart = ln.Substring(0, eqIdx).Trim();
                if (namePart.Length > 0 && !namePart.Contains(" "))
                    startsNewVar = true;
            }

            if (startsNewVar)
            {
                if (!string.IsNullOrEmpty(currentName))
                    locals.Add(new VariableInfo { Name = currentName, Value = currentValue.Trim() });
                currentName = ln.Substring(0, eqIdx).Trim();
                currentValue = ln.Substring(eqIdx + 3);
            }
            else
            {
                // Continuation of a multi-line struct/array value
                if (!string.IsNullOrEmpty(currentName))
                    currentValue += " " + ln.Trim();
            }
        }

        if (!string.IsNullOrEmpty(currentName))
            locals.Add(new VariableInfo { Name = currentName, Value = currentValue.Trim() });

        // Extract type info / clean up pointer-prefixed string values
        foreach (var v in locals)
        {
            // FreeBASIC strings show as: 0x12345 "actual string"
            var strMatch = Regex.Match(v.Value, "^0x[0-9a-fA-F]+ \"(.*)\"$");
            if (strMatch.Success)
            {
                v.Value = "\"" + strMatch.Groups[1].Value + "\"";
                v.DataType = "STRING";
            }
            else if (v.Value.StartsWith("{"))
            {
                v.DataType = "ARRAY/UDT";
            }
            else if (int.TryParse(v.Value, out _))
            {
                v.DataType = "INTEGER";
            }
            else if (double.TryParse(v.Value, System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                v.DataType = "DOUBLE";
            }
        }

        FireOnUI(() => LocalsUpdated?.Invoke(locals));
    }

    /// <summary>Read a GDB/MI field value starting at startPos. Returns (value, newPosition).</summary>
    private static (string, int) ReadMIFieldValue(string data, int startPos)
    {
        if (startPos >= data.Length) return ("", startPos);

        var i = startPos;
        if (data[i] == '"')
        {
            // Quoted string - read until matching unescaped quote
            i += 1;
            var sb = new StringBuilder();
            while (i < data.Length)
            {
                if (data[i] == '\\' && i + 1 < data.Length)
                {
                    sb.Append(data[i]);
                    sb.Append(data[i + 1]);
                    i += 2;
                }
                else if (data[i] == '"')
                {
                    i += 1; // skip closing quote
                    return (sb.ToString(), i);
                }
                else
                {
                    sb.Append(data[i]);
                    i += 1;
                }
            }
            return (sb.ToString(), i);
        }
        else if (data[i] == '{' || data[i] == '[')
        {
            // Nested structure - track brace depth
            var depth = 1;
            var sb = new StringBuilder();
            sb.Append(data[i]);
            i += 1;
            while (i < data.Length && depth > 0)
            {
                if (data[i] == '\\' && i + 1 < data.Length)
                {
                    sb.Append(data[i]);
                    sb.Append(data[i + 1]);
                    i += 2;
                    continue;
                }
                else if (data[i] == '"')
                {
                    // Skip quoted strings inside nested structures
                    sb.Append(data[i]);
                    i += 1;
                    while (i < data.Length)
                    {
                        if (data[i] == '\\' && i + 1 < data.Length)
                        {
                            sb.Append(data[i]);
                            sb.Append(data[i + 1]);
                            i += 2;
                        }
                        else if (data[i] == '"')
                        {
                            sb.Append(data[i]);
                            i += 1;
                            break;
                        }
                        else
                        {
                            sb.Append(data[i]);
                            i += 1;
                        }
                    }
                    continue;
                }
                if (data[i] == '{' || data[i] == '[') depth += 1;
                if (data[i] == '}' || data[i] == ']') depth -= 1;
                sb.Append(data[i]);
                i += 1;
            }
            return (sb.ToString(), i);
        }
        else
        {
            // Unquoted value (number, etc.)
            var sb = new StringBuilder();
            while (i < data.Length && data[i] != ',' && data[i] != '}' && data[i] != ']')
            {
                sb.Append(data[i]);
                i += 1;
            }
            return (sb.ToString(), i);
        }
    }

    /// <summary>Extract a named value from GDB/MI response data.</summary>
    private static string ExtractMIValue(string data, string fieldName)
    {
        var searchStr = fieldName + "=\"";
        var idx = data.IndexOf(searchStr);
        if (idx < 0) return "";
        idx += searchStr.Length;
        var sb = new StringBuilder();
        while (idx < data.Length)
        {
            if (data[idx] == '\\' && idx + 1 < data.Length)
            {
                sb.Append(data[idx]);
                sb.Append(data[idx + 1]);
                idx += 2;
            }
            else if (data[idx] == '"')
            {
                break;
            }
            else
            {
                sb.Append(data[idx]);
                idx += 1;
            }
        }
        return sb.ToString();
    }

    private void ParseStack(string data)
    {
        var frames = new List<StackFrameInfo>();
        foreach (Match m in Regex.Matches(data, @"frame=\{([^}]+)\}"))
        {
            var fd = m.Groups[1].Value;
            var fr = new StackFrameInfo();
            int.TryParse(GetField(fd, "level"), out fr.Level);
            fr.Address = GetField(fd, "addr");
            fr.FunctionName = GetField(fd, "func");
            fr.FilePath = GetField(fd, "fullname");
            if (string.IsNullOrEmpty(fr.FilePath)) fr.FilePath = GetField(fd, "file");
            int.TryParse(GetField(fd, "line"), out fr.LineNumber);
            frames.Add(fr);
        }
        FireOnUI(() => CallStackUpdated?.Invoke(frames));
    }

    // ---- Helpers ----
    private void UpdateBPNumber(string filePath, int lineNumber, int gdbNumber)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        var key = Norm(filePath).ToLowerInvariant();
        if (_breakpoints.ContainsKey(key))
        {
            foreach (var bp in _breakpoints[key])
            {
                if (bp.LineNumber == lineNumber || bp.Number == 0)
                {
                    bp.Number = gdbNumber;
                    bp.Pending = false;
                    break;
                }
            }
        }
    }

    private static string ExtractQuoted(string line, int startIdx)
    {
        if (startIdx >= line.Length || line[startIdx] != '"')
            return startIdx < line.Length ? line.Substring(startIdx) : "";
        var sb = new StringBuilder();
        var i = startIdx + 1;
        while (i < line.Length)
        {
            if (line[i] == '\\' && i + 1 < line.Length)
            {
                switch (line[i + 1])
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    default: sb.Append(line[i]); break;
                }
                i += 2;
            }
            else if (line[i] == '"')
            {
                break;
            }
            else
            {
                sb.Append(line[i]);
                i += 1;
            }
        }
        return sb.ToString();
    }

    private static string GetField(string data, string name)
    {
        var m = Regex.Match(data, name + "=\"([^\"]*?)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string Norm(string path) =>
        string.IsNullOrEmpty(path) ? "" : path.Replace("\\", "/");

    private static string EscGDB(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string UnEsc(string text) =>
        text.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");

    private void FireOnUI(Action action)
    {
        try { _syncCtx.Post(_ => action(), null); }
        catch (Exception ex) { DiagnosticsLogger.LogError("GDBDebugger", "FireOnUI dispatch failed", ex); }
    }

    private void CleanupProcess()
    {
        try
        {
            if (_process != null)
            {
                if (!_process.HasExited) _process.Kill();
                _process.Dispose();
                _process = null;
            }
        }
        catch (Exception ex) { DiagnosticsLogger.LogError("GDBDebugger", "CleanupProcess error", ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopDebugging();
    }
}
