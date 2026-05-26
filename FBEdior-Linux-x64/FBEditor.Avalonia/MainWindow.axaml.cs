using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using FBEditor.Core;
using System.ComponentModel;

namespace FBEditor.Avalonia;

public partial class MainWindow : Window
{
    // ---- One open document per tab ----
    private sealed class DocumentTab
    {
        public readonly TextEditor Editor;
        public TabItem Item = null!;
        public string? Path;
        public bool Dirty;
        public FoldingManager? Folding;
        public DebugLineRenderer DebugRenderer = null!;
        public TextBlock HeaderLabel = null!;
        public DocumentTab(TextEditor e) { Editor = e; }
        public string Title => string.IsNullOrEmpty(Path) ? "untitled" : System.IO.Path.GetFileName(Path);
    }

    private readonly List<DocumentTab> _tabs = new();
    private DocumentTab? _active;
    private bool _initialized;

    private readonly GDBDebugger _debugger = new();
    private string _gdbPath = "";
    private readonly FreeBasicFoldingStrategy _foldingStrategy = new();

    private readonly AIChatManager _ai = new();
    private string _lastReply = "";
    private bool _chatVisible = true;

    private readonly SpellChecker _spell = new();
    private bool _spellEnabled = true;
    private double _fontSize = 14;
    private CompletionWindow? _completionWindow;

    // Most existing logic refers to "the editor" and "the current path"; route both
    // to the active tab so the bulk of the code is unchanged by multi-tab.
    private TextEditor Editor => _active!.Editor;
    private string? _currentPath
    {
        get => _active?.Path;
        set { if (_active != null) { _active.Path = value; UpdateTabHeader(_active); } }
    }

    public MainWindow()
    {
        InitializeComponent();

        Tabs.SelectionChanged += (_, _) =>
        {
            _active = (Tabs.SelectedItem as TabItem)?.Tag as DocumentTab;
            OnActiveTabChanged();
        };

        // Double-click a compiler error to jump to it.
        ErrorsList.DoubleTapped += (_, _) =>
        {
            if (ErrorsList.SelectedItem is ListBoxItem li && li.Tag is CompilerError ce)
                _ = GoToError(ce);
        };

        // File menu
        MnuNew.Click += (_, _) => NewFile();
        MnuOpen.Click += async (_, _) => await OpenFile();
        MnuSave.Click += async (_, _) => await Save();
        MnuSaveAs.Click += async (_, _) => await SaveAs();
        MnuExit.Click += (_, _) => Close();
        // Build menu
        MnuCompile.Click += async (_, _) => await Build(run: false);
        MnuCompileRun.Click += async (_, _) => await Build(run: true);
        // Debug menu
        MnuStart.Click += async (_, _) => await StartOrContinue();
        MnuStop.Click += (_, _) => _debugger.StopDebugging();
        MnuStepOver.Click += (_, _) => _debugger.StepOver();
        MnuStepInto.Click += (_, _) => _debugger.StepInto();
        MnuStepOut.Click += (_, _) => _debugger.StepOut();
        MnuToggleBp.Click += (_, _) => ToggleBreakpointAtCaret();
        // View menu
        MnuRefreshOutline.Click += (_, _) => RefreshOutline();
        MnuToggleChat.Click += (_, _) => ToggleChat();
        MnuToggleSpell.Click += (_, _) => ToggleSpell();
        MnuZoomIn.Click += (_, _) => Zoom(+1);
        MnuZoomOut.Click += (_, _) => Zoom(-1);
        MnuZoomReset.Click += (_, _) => ZoomReset();
        // Help / Settings
        MnuAbout.Click += (_, _) => new AboutWindow().Show();
        MnuSettings.Click += (_, _) => OpenSettings();
        // AI chat
        BtnSend.Click += async (_, _) => await SendChat();
        BtnChatClear.Click += (_, _) => { _ai.ClearHistory(); ChatMessages.Children.Clear(); _lastReply = ""; };
        BtnInsertCode.Click += (_, _) => InsertCode();
        ChatInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                await SendChat();
            }
        };
        // Form designer
        MnuDesigner.Click += (_, _) =>
        {
            var designer = new DesignerWindow(code =>
            {
                var t = AddTab(code, null);
                t.Dirty = true;
                UpdateTabHeader(t);
                StatusFile.Text = "(generated — unsaved)";
            });
            designer.Show();
        };

        // Debug toolbar
        BtnStart.Click += async (_, _) => await StartOrContinue();
        BtnContinue.Click += (_, _) => _debugger.Continue();
        BtnPause.Click += (_, _) => _debugger.Pause();
        BtnStop.Click += (_, _) => _debugger.StopDebugging();
        BtnStepOver.Click += (_, _) => _debugger.StepOver();
        BtnStepInto.Click += (_, _) => _debugger.StepInto();
        BtnStepOut.Click += (_, _) => _debugger.StepOut();
        BtnToggleBp.Click += (_, _) => ToggleBreakpointAtCaret();

        // Click a stack frame to switch scope.
        CallStackList.SelectionChanged += (_, _) =>
        {
            if (CallStackList.SelectedItem is ListBoxItem li && li.Tag is int lvl)
                _debugger.SelectFrame(lvl);
        };

        WireDebuggerEvents();

        AppGlobals.InitializeApp();
        AppGlobals.LoadSettings();
        if (string.IsNullOrEmpty(AppGlobals.Build.FBCPath))
            AppGlobals.Build.FBCPath = AppGlobals.FindFBCPath();
        _gdbPath = GDBDebugger.FindGDBPath();
        _spell.AutoLoad();
        _fontSize = Math.Clamp(AppGlobals.Settings.EditorFontSize, 6, 48);
        _spellEnabled = AppGlobals.Settings.SpellCheckEnabled;
        _initialized = true;

        RebuildRecentMenu();
        NewFile();
    }

    private bool _forceClose;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Guard the whole-app close: prompt for each unsaved tab.
        if (_forceClose) return;
        if (_tabs.All(t => !t.Dirty)) return;
        e.Cancel = true;
        _ = PromptCloseAll();
    }

    private async Task PromptCloseAll()
    {
        foreach (var t in _tabs.Where(t => t.Dirty).ToList())
        {
            Tabs.SelectedItem = t.Item;
            var choice = await new ConfirmSaveDialog(t.Title).ShowDialog<SaveChoice>(this);
            if (choice == SaveChoice.Cancel) return;       // abort closing entirely
            if (choice == SaveChoice.Save && !await Save()) return;
            t.Dirty = false;
        }
        _forceClose = true;
        Close();
    }

    // ---- Keyboard shortcuts (Avalonia MenuItem.InputGesture is display-only) ----
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.F5: if (shift) _debugger.StopDebugging(); else _ = StartOrContinue(); e.Handled = true; break;
            case Key.F9: ToggleBreakpointAtCaret(); e.Handled = true; break;
            case Key.F10: _debugger.StepOver(); e.Handled = true; break;
            case Key.F11: if (shift) _debugger.StepOut(); else _debugger.StepInto(); e.Handled = true; break;
            case Key.F6: _ = Build(run: true); e.Handled = true; break;
            case Key.F4: RefreshOutline(); e.Handled = true; break;
            case Key.N when ctrl: NewFile(); e.Handled = true; break;
            case Key.O when ctrl: _ = OpenFile(); e.Handled = true; break;
            case Key.S when ctrl: _ = Save(); e.Handled = true; break;
            case Key.W when ctrl: if (_active != null) _ = CloseTab(_active); e.Handled = true; break;
            case Key.OemPlus when ctrl: Zoom(+1); e.Handled = true; break;
            case Key.Add when ctrl: Zoom(+1); e.Handled = true; break;
            case Key.OemMinus when ctrl: Zoom(-1); e.Handled = true; break;
            case Key.Subtract when ctrl: Zoom(-1); e.Handled = true; break;
            case Key.D0 when ctrl: ZoomReset(); e.Handled = true; break;
        }
    }

    // ---- Tab management ----

    private DocumentTab AddTab(string content, string? path)
    {
        var editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Code,DejaVu Sans Mono,Consolas,monospace"),
            FontSize = _fontSize,
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4"))
        };

        var tab = new DocumentTab(editor) { Path = path };
        editor.Text = content;  // set before wiring TextChanged so it doesn't count as a dirty edit
        // Syntax highlighting only for code files (not .txt/.md/...). Spell-check covers the
        // whole document for text files, but only comments/strings for code.
        editor.TextArea.TextView.LineTransformers.Add(new FreeBasicColorizer(() => !IsTextFile(tab.Path)));
        editor.TextArea.TextView.BackgroundRenderers.Add(
            new SpellCheckRenderer(_spell, () => _spellEnabled, () => IsTextFile(tab.Path)));
        tab.DebugRenderer = new DebugLineRenderer { IsBreakpoint = line => LineHasBreakpoint(tab, line) };
        editor.TextArea.TextView.BackgroundRenderers.Add(tab.DebugRenderer);
        tab.Folding = FoldingManager.Install(editor.TextArea);

        // Autocomplete: pop a completion list as the user types identifiers (code files only).
        editor.TextArea.TextEntered += (_, e) => OnTextEntered(editor, tab, e);
        // Right-click spelling suggestions.
        editor.TextArea.RightClickMovesCaret = true;
        var spellMenu = new ContextMenu();
        spellMenu.Opening += (_, e) => BuildSpellMenu(editor, tab, spellMenu, e);
        editor.ContextMenu = spellMenu;

        editor.Document.TextChanged += (_, _) =>
        {
            tab.Dirty = true;
            UpdateTabHeader(tab);
            if (ReferenceEquals(tab, _active)) UpdateFoldings();
        };
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (ReferenceEquals(tab, _active))
            {
                var c = editor.TextArea.Caret;
                StatusCaret.Text = $"Ln {c.Line}, Col {c.Column}";
            }
        };

        var item = new TabItem { Header = BuildTabHeader(tab), Content = editor, Tag = tab };
        tab.Item = item;
        Tabs.Items.Add(item);
        _tabs.Add(tab);
        Tabs.SelectedItem = item;   // fires SelectionChanged -> sets _active, OnActiveTabChanged
        UpdateTabHeader(tab);
        UpdateFoldings();
        return tab;
    }

    private Control BuildTabHeader(DocumentTab tab)
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        tab.HeaderLabel = label;
        var close = new Button
        {
            Content = "✕",
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 11
        };
        close.Click += (_, _) => _ = CloseTab(tab);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(label);
        sp.Children.Add(close);
        return sp;
    }

    private void UpdateTabHeader(DocumentTab tab)
    {
        tab.HeaderLabel.Text = (tab.Dirty ? "• " : "") + tab.Title;
        if (ReferenceEquals(tab, _active))
        {
            StatusFile.Text = string.IsNullOrEmpty(tab.Path) ? "(untitled)" : tab.Path;
            Title = string.IsNullOrEmpty(tab.Path) ? "FBEditor" : $"FBEditor — {Path.GetFileName(tab.Path)}";
        }
    }

    private async Task CloseTab(DocumentTab tab)
    {
        if (tab.Dirty)
        {
            Tabs.SelectedItem = tab.Item; // bring it to front so the user sees what's being closed
            var choice = await new ConfirmSaveDialog(tab.Title).ShowDialog<SaveChoice>(this);
            if (choice == SaveChoice.Cancel) return;
            if (choice == SaveChoice.Save && !await Save()) return; // Save cancelled (e.g. Save As dialog dismissed)
        }
        int idx = _tabs.IndexOf(tab);
        Tabs.Items.Remove(tab.Item);
        _tabs.Remove(tab);
        if (_tabs.Count == 0)
            AddTab("' New FreeBASIC program\n\n", null);
        else
            Tabs.SelectedItem = _tabs[Math.Max(0, idx - 1)].Item;
    }

    private void OnActiveTabChanged()
    {
        if (_active == null)
        {
            StatusFile.Text = "(no file)";
            StatusCaret.Text = "Ln 1, Col 1";
            Title = "FBEditor";
            return;
        }
        var c = _active.Editor.TextArea.Caret;
        StatusCaret.Text = $"Ln {c.Line}, Col {c.Column}";
        UpdateTabHeader(_active);
        RefreshOutline();
        UpdateFoldings();
    }

    private DocumentTab? TabForPath(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); } catch { full = path; }
        return _tabs.FirstOrDefault(t =>
        {
            if (string.IsNullOrEmpty(t.Path)) return false;
            try { return string.Equals(Path.GetFullPath(t.Path), full, StringComparison.Ordinal); }
            catch { return string.Equals(t.Path, path, StringComparison.Ordinal); }
        });
    }

    // ---- File operations ----

    private void NewFile() => AddTab("' New FreeBASIC program\n\n", null);

    private async Task OpenFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open FreeBASIC source",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("FreeBASIC") { Patterns = new[] { "*.bas", "*.bi" } },
                new FilePickerFileType("Text") { Patterns = new[] { "*.txt", "*.md", "*.log" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;
        OpenPath(path);
    }

    private void OpenPath(string path)
    {
        if (!File.Exists(path))
        {
            ShowOutput("File not found: " + path);
            return;
        }
        var existing = TabForPath(path);
        if (existing != null) { Tabs.SelectedItem = existing.Item; return; }

        AddTab(AppGlobals.ReadFileWithEncoding(path, out _), path);
        AppGlobals.AddRecentFile(path);
        AppGlobals.SaveSettings();
        RebuildRecentMenu();
    }

    private void RebuildRecentMenu()
    {
        MnuRecent.Items.Clear();
        if (AppGlobals.RecentFiles.Count == 0)
        {
            MnuRecent.Items.Add(new MenuItem { Header = "(no recent files)", IsEnabled = false });
            return;
        }
        foreach (var path in AppGlobals.RecentFiles)
        {
            var item = new MenuItem { Header = path };
            var captured = path;
            item.Click += (_, _) => OpenPath(captured);
            MnuRecent.Items.Add(item);
        }
        MnuRecent.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear recent files" };
        clear.Click += (_, _) => { AppGlobals.RecentFiles.Clear(); AppGlobals.SaveSettings(); RebuildRecentMenu(); };
        MnuRecent.Items.Add(clear);
    }

    private async Task<bool> Save()
    {
        if (_active == null) return false;
        if (string.IsNullOrEmpty(_active.Path)) return await SaveAs();
        File.WriteAllText(_active.Path, Editor.Text);
        _active.Dirty = false;
        UpdateTabHeader(_active);
        StatusBuild.Text = "Saved";
        return true;
    }

    private async Task<bool> SaveAs()
    {
        if (_active == null) return false;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save FreeBASIC source",
            DefaultExtension = "bas",
            SuggestedFileName = "untitled.bas",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("FreeBASIC") { Patterns = new[] { "*.bas", "*.bi" } }
            }
        });
        if (file is null) return false;
        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return false;
        File.WriteAllText(path, Editor.Text);
        _active.Path = path;
        _active.Dirty = false;
        UpdateTabHeader(_active);
        StatusFile.Text = path;
        AppGlobals.AddRecentFile(path);
        AppGlobals.SaveSettings();
        RebuildRecentMenu();
        _active.Editor.TextArea.TextView.Redraw(); // re-evaluate highlight/spell for the new extension
        return true;
    }

    // ---- Build ----

    private async Task Build(bool run)
    {
        if (!_initialized || _active == null) return;
        if (string.IsNullOrEmpty(_currentPath)) { if (!await SaveAs()) return; }
        else await Save();

        if (string.IsNullOrEmpty(AppGlobals.Build.FBCPath))
        {
            ShowOutput("ERROR: fbc not found. Set the compiler path in settings.");
            return;
        }

        AppGlobals.Build.DebugInfo = false;
        AppGlobals.Build.TargetType = Editor.Text.Contains("window9.bi", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        StatusBuild.Text = "Building...";
        ShowOutput("Building " + Path.GetFileName(_currentPath) + " ...");
        BottomTabs.SelectedIndex = 0;

        var path = _currentPath!;
        var result = await Task.Run(() => BuildSystem.BuildFile(path, runAfter: run));

        OutputBox.Text = result.Output;
        StatusBuild.Text = result.Success ? $"Build OK ({result.Duration:F2}s)" : "Build FAILED";
        PopulateErrors(result.Output, Path.GetDirectoryName(path) ?? "");
    }

    // ---- Debug ----

    private async Task StartOrContinue()
    {
        if (_debugger.IsRunning)
        {
            if (_debugger.IsPaused) _debugger.Continue();
            return;
        }
        if (_active == null) return;

        if (string.IsNullOrEmpty(_gdbPath))
        {
            ShowOutput("ERROR: gdb not found on PATH. Install gdb to debug.");
            BottomTabs.SelectedIndex = 0;
            return;
        }

        if (string.IsNullOrEmpty(_currentPath)) { if (!await SaveAs()) return; }
        else await Save();

        AppGlobals.Build.DebugInfo = true;
        AppGlobals.Build.TargetType = Editor.Text.Contains("window9.bi", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        StatusBuild.Text = "Building (debug)...";
        ShowOutput("Building " + Path.GetFileName(_currentPath) + " with -g ...");
        BottomTabs.SelectedIndex = 0;

        var path = _currentPath!;
        var result = await Task.Run(() => BuildSystem.BuildFile(path, runAfter: false));
        OutputBox.Text = result.Output;
        if (!result.Success) { StatusBuild.Text = "Build FAILED"; PopulateErrors(result.Output, Path.GetDirectoryName(path) ?? ""); return; }

        var exe = BuildSystem.GetOutputExePath(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        LocalsList.Items.Clear();
        CallStackList.Items.Clear();

        if (_debugger.StartDebugging(_gdbPath, exe, path, dir))
        {
            StatusBuild.Text = "Debugging...";
            _debugger.Run();
        }
    }

    private void ToggleBreakpointAtCaret()
    {
        if (_active == null || string.IsNullOrEmpty(_active.Path))
        {
            ShowOutput("Save the file before setting breakpoints.");
            BottomTabs.SelectedIndex = 0;
            return;
        }
        int line = Editor.TextArea.Caret.Line;
        _debugger.ToggleBreakpoint(_active.Path, line);
        _active.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private bool LineHasBreakpoint(DocumentTab tab, int line) =>
        !string.IsNullOrEmpty(tab.Path) && _debugger.HasBreakpoint(tab.Path, line);

    private void WireDebuggerEvents()
    {
        _debugger.DebugOutput += t => OnUi(() => AppendOutput(t));
        _debugger.DebugError += t => OnUi(() => AppendOutput("[ERR] " + t));

        _debugger.DebugPaused += (file, line) => OnUi(() =>
        {
            var tab = TabForPath(file);
            if (tab == null && File.Exists(file))
                tab = AddTab(AppGlobals.ReadFileWithEncoding(file, out _), file);
            if (tab == null) return;

            Tabs.SelectedItem = tab.Item;
            foreach (var t in _tabs)
            {
                t.DebugRenderer.CurrentLine = ReferenceEquals(t, tab) ? line : 0;
                t.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            }
            if (line >= 1 && line <= tab.Editor.Document.LineCount) tab.Editor.ScrollToLine(line);
        });

        _debugger.DebugResumed += () => OnUi(ClearCurrentLineAll);
        _debugger.DebugStopped += () => OnUi(() => { ClearCurrentLineAll(); StatusBuild.Text = "Debug ended"; });

        _debugger.LocalsUpdated += locals => OnUi(() =>
        {
            LocalsList.Items.Clear();
            foreach (var v in locals)
            {
                if (IsCompilerInternal(v.Name)) continue;
                var t = string.IsNullOrEmpty(v.DataType) ? "" : $"   ({v.DataType})";
                LocalsList.Items.Add($"{v.Name} = {v.Value}{t}");
            }
        });

        _debugger.CallStackUpdated += frames => OnUi(() =>
        {
            CallStackList.Items.Clear();
            foreach (var fr in frames)
            {
                CallStackList.Items.Add(new ListBoxItem
                {
                    Content = $"#{fr.Level}  {fr.FunctionName}  {Path.GetFileName(fr.FilePath)}:{fr.LineNumber}",
                    Tag = fr.Level
                });
            }
        });

        _debugger.BreakpointHit += (_, _, _) => OnUi(() => BottomTabs.SelectedIndex = 1);
    }

    private void ClearCurrentLineAll()
    {
        foreach (var t in _tabs)
        {
            t.DebugRenderer.CurrentLine = 0;
            t.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    // ---- Outline ----

    private void RefreshOutline()
    {
        OutlineTree.Items.Clear();
        if (_active == null) return;
        var items = CodeOutline.ParseOutline(Editor.Text);
        foreach (var group in items.GroupBy(o => o.Category).OrderBy(g => g.Key))
        {
            var cat = new TreeViewItem { Header = $"{group.Key} ({group.Count()})", IsExpanded = true };
            foreach (var it in group)
            {
                var dt = string.IsNullOrEmpty(it.DataType) ? "" : " : " + it.DataType;
                var leaf = new TreeViewItem { Header = $"[{it.Icon}] {it.Name}{dt}", Tag = it.LineNumber };
                leaf.DoubleTapped += (_, _) => JumpToLine((int)leaf.Tag!);
                cat.Items.Add(leaf);
            }
            OutlineTree.Items.Add(cat);
        }
    }

    private void JumpToLine(int lineNumber)
    {
        if (_active == null || lineNumber < 1 || lineNumber > Editor.Document.LineCount) return;
        Editor.ScrollToLine(lineNumber);
        var docLine = Editor.Document.GetLineByNumber(lineNumber);
        Editor.TextArea.Caret.Line = lineNumber;
        Editor.TextArea.Caret.Column = 1;
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Select(docLine.Offset, docLine.Length);
        Editor.Focus();
    }

    // ---- Helpers ----

    private void ShowOutput(string text)
    {
        OutputBox.Text = text + "\n";
        BottomTabs.SelectedIndex = 0;
    }

    private void AppendOutput(string text)
    {
        OutputBox.Text += text.EndsWith("\n") ? text : text + "\n";
        OutputBox.CaretIndex = OutputBox.Text.Length;
    }

    // ---- AI chat ----

    private async Task SendChat()
    {
        var text = ChatInput.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _ai.IsBusy) return;

        AddMessage("user", text);
        ChatInput.Text = "";
        var placeholder = AddMessage("assistant", "…");
        BtnSend.IsEnabled = false;

        bool include = ChatInclude.IsChecked == true;
        string code = include ? Editor.Text : "";
        string fname = include ? (Path.GetFileName(_currentPath) ?? "untitled.bas") : "";

        string reply;
        try { reply = await _ai.SendMessageAsync(text, include, code, fname); }
        catch (Exception ex) { reply = "Error: " + ex.Message; }

        placeholder.Text = reply;
        _lastReply = reply;
        BtnSend.IsEnabled = true;
        ScrollChatToEnd();
    }

    private void InsertCode()
    {
        if (_active == null || string.IsNullOrEmpty(_lastReply)) return;
        var code = AIChatManager.ExtractCodeFromResponse(_lastReply);
        if (string.IsNullOrEmpty(code))
        {
            AddMessage("assistant", "(no FreeBASIC code block found in the last reply)");
            return;
        }
        Editor.Document.Insert(Editor.CaretOffset, code);
        Editor.Focus();
    }

    private TextBlock AddMessage(string role, string text)
    {
        bool user = role == "user";
        var tb = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Margin = new Thickness(9, 6)
        };
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.Parse(user ? "#0E639C" : "#3A3D41")),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(user ? 36 : 6, 3, user ? 6 : 36, 3),
            Child = tb
        };
        ChatMessages.Children.Add(bubble);
        ScrollChatToEnd();
        return tb;
    }

    private void ScrollChatToEnd() =>
        Dispatcher.UIThread.Post(
            () => ChatScroll.Offset = new Vector(0, ChatScroll.Extent.Height),
            DispatcherPriority.Background);

    private void ToggleChat()
    {
        _chatVisible = !_chatVisible;
        MainGrid.ColumnDefinitions[4].Width = _chatVisible ? new GridLength(340) : new GridLength(0);
        MainGrid.ColumnDefinitions[3].Width = _chatVisible ? new GridLength(4) : new GridLength(0);
        ChatPanel.IsVisible = _chatVisible;
        ChatSplitter.IsVisible = _chatVisible;
    }

    private void ToggleSpell()
    {
        _spellEnabled = !_spellEnabled;
        if (_spellEnabled && !_spell.Enabled)
            ShowOutput("Spell-check: no dictionary found. Install 'hunspell-en-us', " +
                       "or place en_US.aff and en_US.dic in ~/.config/FBEditor/dict/");
        foreach (var t in _tabs)
            t.Editor.TextArea.TextView.Redraw();
    }

    // ---- Zoom ----

    private void Zoom(int delta)
    {
        _fontSize = Math.Clamp(_fontSize + delta, 6, 48);
        ApplyFontToAll();
    }

    private void ZoomReset()
    {
        _fontSize = 14;
        ApplyFontToAll();
    }

    private void ApplyFontToAll()
    {
        foreach (var t in _tabs) t.Editor.FontSize = _fontSize;
        AppGlobals.Settings.EditorFontSize = (int)Math.Round(_fontSize);
        AppGlobals.SaveSettings();
    }

    // ---- Settings / text files ----

    private void OpenSettings()
    {
        var win = new SettingsWindow(
            _gdbPath,
            onApplied: () =>
            {
                _fontSize = Math.Clamp(AppGlobals.Settings.EditorFontSize, 6, 48);
                _spellEnabled = AppGlobals.Settings.SpellCheckEnabled;
                ApplyFontToAll();
                foreach (var t in _tabs) t.Editor.TextArea.TextView.Redraw();
            },
            onGdbPath: p => _gdbPath = p);
        win.Show();
    }

    private static bool IsTextFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".txt" or ".text" or ".md" or ".markdown" or ".log" or ".nfo" or ".me" or ".readme";
    }

    // ---- Autocomplete ----

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void OnTextEntered(TextEditor editor, DocumentTab tab, TextInputEventArgs e)
    {
        // Code files only; skip plain-text documents.
        if (IsTextFile(tab.Path)) return;
        if (string.IsNullOrEmpty(e.Text) || !char.IsLetter(e.Text[0])) return;

        var doc = editor.Document;
        int caret = editor.CaretOffset;
        int start = caret;
        while (start > 0 && IsWordChar(doc.GetCharAt(start - 1))) start--;
        string prefix = doc.GetText(start, caret - start);
        if (prefix.Length < 2) return; // wait until there's something to match

        var matches = SyntaxConfig.AutoCompleteWords
            .Where(w => w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        !w.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(60)
            .ToList();
        if (matches.Count == 0) { _completionWindow?.Close(); return; }

        var cw = new CompletionWindow(editor.TextArea) { CloseAutomatically = true };
        cw.StartOffset = start;
        cw.EndOffset = caret;
        foreach (var m in matches)
            cw.CompletionList.CompletionData.Add(new FbCompletionData(m, "FreeBASIC keyword"));
        cw.Closed += (_, _) => _completionWindow = null;
        cw.Show();
        _completionWindow = cw;
    }

    // ---- Right-click spelling suggestions ----

    private void BuildSpellMenu(TextEditor editor, DocumentTab tab, ContextMenu menu, CancelEventArgs e)
    {
        menu.Items.Clear();
        var doc = editor.Document;
        int caret = editor.CaretOffset;

        // Word boundaries around the caret (letters only — apostrophe starts a comment in FB).
        int start = caret, end = caret;
        while (start > 0 && char.IsLetter(doc.GetCharAt(start - 1))) start--;
        while (end < doc.TextLength && char.IsLetter(doc.GetCharAt(end))) end++;
        string word = end > start ? doc.GetText(start, end - start) : "";

        bool offerSpelling = false;
        if (_spellEnabled && _spell.Enabled && word.Length >= 3)
        {
            // Only where a squiggle would appear: whole document for text files,
            // comments/strings only for code.
            bool checkable = IsTextFile(tab.Path);
            if (!checkable)
            {
                var line = doc.GetLineByOffset(start);
                string lineText = doc.GetText(line);
                int col = start - line.Offset;
                checkable = SpellCheckRenderer.CheckableSpans(lineText).Any(s => col >= s.start && col < s.end);
            }
            try { offerSpelling = checkable && !_spell.Check(word); } catch { }
        }

        if (offerSpelling)
        {
            var suggestions = _spell.Suggest(word).Take(7).ToList();
            if (suggestions.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "(no suggestions)", IsEnabled = false });
            }
            else
            {
                foreach (var s in suggestions)
                {
                    var sug = s;
                    var mi = new MenuItem { Header = sug };
                    mi.Click += (_, _) => doc.Replace(start, end - start, sug);
                    menu.Items.Add(mi);
                }
            }
            menu.Items.Add(new Separator());
        }

        // Standard edit actions, always present.
        var cut = new MenuItem { Header = "Cut" };
        cut.Click += (_, _) => editor.Cut();
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => editor.Copy();
        var paste = new MenuItem { Header = "Paste" };
        paste.Click += (_, _) => editor.Paste();
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
    }

    private void UpdateFoldings()
    {
        if (_active?.Folding != null && _active.Editor.Document != null)
            _foldingStrategy.UpdateFoldings(_active.Folding, _active.Editor.Document);
    }

    private void PopulateErrors(string buildOutput, string baseDir)
    {
        ErrorsList.Items.Clear();
        var diags = AppGlobals.ParseCompilerErrors(buildOutput, baseDir);
        foreach (var ce in diags)
            ErrorsList.Items.Add(new ListBoxItem { Content = ce.ToString(), Tag = ce });

        if (diags.Any(d => d.ErrorType.Equals("error", StringComparison.OrdinalIgnoreCase)))
            BottomTabs.SelectedIndex = 3;
    }

    private async Task GoToError(CompilerError ce)
    {
        if (!string.IsNullOrEmpty(ce.FilePath) && File.Exists(ce.FilePath))
        {
            var tab = TabForPath(ce.FilePath) ?? AddTab(AppGlobals.ReadFileWithEncoding(ce.FilePath, out _), ce.FilePath);
            Tabs.SelectedItem = tab.Item;
        }
        await Task.Yield();
        if (ce.LineNumber >= 1) JumpToLine(ce.LineNumber);
    }

    // FreeBASIC emits compiler-internal locals (__FB_ARGC__$0, fb$result$0, vr$3, tmp...).
    private static bool IsCompilerInternal(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (name.StartsWith("__FB_", StringComparison.Ordinal)) return true;
        if (name.StartsWith("fb$", StringComparison.Ordinal)) return true;
        if (name.StartsWith("vr$", StringComparison.Ordinal)) return true;
        if (name.StartsWith("tmp", StringComparison.OrdinalIgnoreCase) && name.Contains('$')) return true;
        return false;
    }

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a();
        else Dispatcher.UIThread.Post(a);
    }
}
