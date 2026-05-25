using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FBEditor.Core;

namespace FBEditor.Avalonia;

/// <summary>
/// Visual Window9 form designer. Builds a W9FormDesign by direct manipulation
/// (click toolbox to add, drag to move, drag handles to resize, edit properties)
/// and feeds it to W9CodeGenerator.GenerateCode.
/// </summary>
public partial class DesignerWindow : Window
{
    private readonly W9FormDesign _design = new()
    {
        FormTitle = "My Window9 Form",
        FormWidth = 480, FormHeight = 360,
        BaseWidth = 480, BaseHeight = 360,
        HasKeyboardShortcut = false
    };

    private readonly Dictionary<W9GadgetInstance, Border> _controls = new();
    private readonly Dictionary<W9GadgetType, int> _typeCounters = new();
    private readonly Action<string> _onGenerate;

    private W9GadgetInstance? _selected;
    private W9GadgetInstance? _dragGadget;
    private Point _dragStart;
    private int _dragOrigX, _dragOrigY;
    private bool _suppress;
    private W9GadgetType? _pendingType;

    // Resize handles (8: NW N NE E SE S SW W)
    private readonly Rectangle[] _handles = new Rectangle[8];
    private int _resizeRole = -1;
    private Point _resizeStart;
    private int _origX, _origY, _origW, _origH;
    private const int HandleSize = 8;

    private static readonly (double fx, double fy)[] HandlePos =
    {
        (0, 0), (0.5, 0), (1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (0, 0.5)
    };
    private static readonly StandardCursorType[] HandleCursors =
    {
        StandardCursorType.TopLeftCorner, StandardCursorType.SizeNorthSouth, StandardCursorType.TopRightCorner,
        StandardCursorType.SizeWestEast, StandardCursorType.BottomRightCorner, StandardCursorType.SizeNorthSouth,
        StandardCursorType.BottomLeftCorner, StandardCursorType.SizeWestEast
    };

    // Property fields
    private TextBox _fldName = null!, _fldText = null!, _fldItems = null!;
    private TextBox _fldX = null!, _fldY = null!, _fldW = null!, _fldH = null!;
    private TextBox _fldBack = null!, _fldFore = null!, _fldFontName = null!, _fldFontSize = null!;
    private CheckBox _chkPassword = null!, _chkChecked = null!;
    private CheckBox _chkOnClick = null!, _chkOnChange = null!, _chkOnDbl = null!;

    private static readonly IBrush SelBrush = new SolidColorBrush(Color.Parse("#1E88E5"));
    private static readonly IBrush UnselBrush = new SolidColorBrush(Color.Parse("#999999"));
    private static readonly IBrush HandleFill = new SolidColorBrush(Color.Parse("#1E88E5"));

    public DesignerWindow(Action<string> onGenerate)
    {
        InitializeComponent();
        _onGenerate = onGenerate;

        foreach (var def in W9GadgetRegistry.AllTypes)
            Toolbox.Items.Add(new ListBoxItem { Content = def.DisplayName, Tag = def.GadgetType });
        Toolbox.SelectionChanged += (_, _) =>
        {
            if (Toolbox.SelectedItem is ListBoxItem li && li.Tag is W9GadgetType gt)
            {
                _pendingType = gt;
                var d = W9GadgetRegistry.GetTypeDef(gt);
                DesignerStatus.Text = $"Click on the form to place {d?.DisplayName ?? gt.ToString()} (Esc to cancel)";
                DesignCanvas.Cursor = new Cursor(StandardCursorType.Cross);
            }
        };

        FormTitleBox.Text = _design.FormTitle;
        FormWidthBox.Text = _design.FormWidth.ToString();
        FormHeightBox.Text = _design.FormHeight.ToString();
        FormTitleBox.TextChanged += (_, _) => _design.FormTitle = FormTitleBox.Text ?? "";
        FormWidthBox.TextChanged += (_, _) => { _design.FormWidth = _design.BaseWidth = ParseInt(FormWidthBox.Text, _design.FormWidth); ResizeCanvas(); };
        FormHeightBox.TextChanged += (_, _) => { _design.FormHeight = _design.BaseHeight = ParseInt(FormHeightBox.Text, _design.FormHeight); ResizeCanvas(); };

        BuildHandles();
        BuildPropFields();
        UpdatePropFields();

        BtnDelete.Click += (_, _) => DeleteSelected();
        BtnGenerate.Click += (_, _) => Generate();
        BtnMenu.Click += (_, _) => new MenuEditorWindow(_design).Show();

        DesignCanvas.PointerPressed += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, DesignCanvas)) return;
            if (_pendingType is W9GadgetType gt)
            {
                var p = e.GetPosition(DesignCanvas);
                AddGadget(gt, (int)p.X, (int)p.Y);
                Disarm();
            }
            else Select(null);
        };

        ResizeCanvas();
    }

    // ---- Gadget creation / rendering ----

    private void AddGadget(W9GadgetType gt, int x, int y)
    {
        var def = W9GadgetRegistry.GetTypeDef(gt);
        int cnt = _typeCounters.TryGetValue(gt, out var c) ? c + 1 : 1;
        _typeCounters[gt] = cnt;

        var g = new W9GadgetInstance
        {
            ID = _design.GetNextGadgetID(),
            GadgetType = gt,
            EnumName = $"gi{gt}{cnt}",
            Text = def?.DefaultText ?? "",
            X = Math.Max(0, x), Y = Math.Max(0, y),
            W = def?.DefaultWidth ?? 100,
            H = def?.DefaultHeight ?? 30
        };
        if (gt == W9GadgetType.ListBox || gt == W9GadgetType.ComboBox)
            g.Items = "Item 1\nItem 2\nItem 3";

        _design.Gadgets.Add(g);
        var border = MakeControl(g);
        _controls[g] = border;
        DesignCanvas.Children.Add(border);
        Select(g);
    }

    private void Disarm()
    {
        _pendingType = null;
        Toolbox.SelectedItem = null;
        DesignCanvas.Cursor = Cursor.Default;
        DesignerStatus.Text = "";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Escape && _pendingType != null) { Disarm(); e.Handled = true; }
        else if (e.Key == Key.Delete && _selected != null) { DeleteSelected(); e.Handled = true; }
    }

    private Border MakeControl(W9GadgetInstance g)
    {
        var border = new Border
        {
            Width = g.W, Height = g.H,
            BorderBrush = UnselBrush, BorderThickness = new Thickness(1),
            Tag = g,
            Child = new TextBlock
            {
                Margin = new Thickness(5, 2, 5, 2),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
        Canvas.SetLeft(border, g.X);
        Canvas.SetTop(border, g.Y);
        border.PointerPressed += Gadget_PointerPressed;
        border.PointerMoved += Gadget_PointerMoved;
        border.PointerReleased += Gadget_PointerReleased;
        ApplyVisual(g, border);
        return border;
    }

    private void Gadget_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border b || b.Tag is not W9GadgetInstance g) return;
        Select(g);
        _dragGadget = g;
        _dragStart = e.GetPosition(DesignCanvas);
        _dragOrigX = g.X; _dragOrigY = g.Y;
        e.Pointer.Capture(b);
        e.Handled = true;
    }

    private void Gadget_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragGadget is null || sender is not Border b || b.Tag is not W9GadgetInstance g || !ReferenceEquals(g, _dragGadget))
            return;
        var p = e.GetPosition(DesignCanvas);
        int nx = Math.Max(0, _dragOrigX + (int)(p.X - _dragStart.X));
        int ny = Math.Max(0, _dragOrigY + (int)(p.Y - _dragStart.Y));
        g.X = nx; g.Y = ny;
        Canvas.SetLeft(b, nx);
        Canvas.SetTop(b, ny);
        PositionHandles();
        if (ReferenceEquals(_selected, g)) UpdatePropFields();
    }

    private void Gadget_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragGadget = null;
        e.Pointer.Capture(null);
    }

    // ---- Resize handles ----

    private void BuildHandles()
    {
        for (int i = 0; i < 8; i++)
        {
            var h = new Rectangle
            {
                Width = HandleSize, Height = HandleSize,
                Fill = HandleFill, IsVisible = false,
                ZIndex = 1000, Tag = i,
                Cursor = new Cursor(HandleCursors[i])
            };
            int role = i;
            h.PointerPressed += (_, e) =>
            {
                if (_selected is null) return;
                _resizeRole = role;
                _resizeStart = e.GetPosition(DesignCanvas);
                _origX = _selected.X; _origY = _selected.Y; _origW = _selected.W; _origH = _selected.H;
                e.Pointer.Capture(h);
                e.Handled = true;
            };
            h.PointerMoved += (_, e) =>
            {
                if (_resizeRole != role || _selected is null) return;
                ResizeFromHandle(e.GetPosition(DesignCanvas));
            };
            h.PointerReleased += (_, e) =>
            {
                _resizeRole = -1;
                e.Pointer.Capture(null);
            };
            _handles[i] = h;
            DesignCanvas.Children.Add(h);
        }
    }

    private void ResizeFromHandle(Point p)
    {
        if (_selected is null) return;
        int dx = (int)(p.X - _resizeStart.X);
        int dy = (int)(p.Y - _resizeStart.Y);

        bool left = _resizeRole is 0 or 6 or 7;
        bool right = _resizeRole is 2 or 3 or 4;
        bool top = _resizeRole is 0 or 1 or 2;
        bool bottom = _resizeRole is 4 or 5 or 6;

        int nx = _origX, ny = _origY, nw = _origW, nh = _origH;
        if (left) { nx = _origX + dx; nw = _origW - dx; }
        if (right) { nw = _origW + dx; }
        if (top) { ny = _origY + dy; nh = _origH - dy; }
        if (bottom) { nh = _origH + dy; }

        if (nw < 16) { if (left) nx = _origX + _origW - 16; nw = 16; }
        if (nh < 12) { if (top) ny = _origY + _origH - 12; nh = 12; }
        if (nx < 0) nx = 0;
        if (ny < 0) ny = 0;

        _selected.X = nx; _selected.Y = ny; _selected.W = nw; _selected.H = nh;
        if (_controls.TryGetValue(_selected, out var b))
        {
            Canvas.SetLeft(b, nx); Canvas.SetTop(b, ny);
            b.Width = nw; b.Height = nh;
        }
        PositionHandles();
        UpdatePropFields();
    }

    private void PositionHandles()
    {
        if (_selected is null)
        {
            foreach (var h in _handles) h.IsVisible = false;
            return;
        }
        for (int i = 0; i < 8; i++)
        {
            var (fx, fy) = HandlePos[i];
            double x = _selected.X + fx * _selected.W - HandleSize / 2.0;
            double y = _selected.Y + fy * _selected.H - HandleSize / 2.0;
            Canvas.SetLeft(_handles[i], x);
            Canvas.SetTop(_handles[i], y);
            _handles[i].IsVisible = true;
        }
    }

    // ---- Selection ----

    private void Select(W9GadgetInstance? g)
    {
        _selected = g;
        foreach (var kv in _controls)
        {
            bool sel = ReferenceEquals(kv.Key, g);
            kv.Value.BorderBrush = sel ? SelBrush : UnselBrush;
            kv.Value.BorderThickness = new Thickness(sel ? 2 : 1);
        }
        PositionHandles();
        UpdatePropFields();
    }

    private void DeleteSelected()
    {
        if (_selected is null) return;
        if (_controls.TryGetValue(_selected, out var b)) DesignCanvas.Children.Remove(b);
        _controls.Remove(_selected);
        _design.Gadgets.Remove(_selected);
        Select(null);
    }

    // ---- Property panel ----

    private void BuildPropFields()
    {
        _fldName = AddField("Name (enum)");
        _fldText = AddField("Text / Caption");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        _fldX = MiniField(grid, 0, 0, "X");
        _fldY = MiniField(grid, 0, 1, "Y");
        _fldW = MiniField(grid, 1, 0, "W");
        _fldH = MiniField(grid, 1, 1, "H");
        PropPanel.Children.Add(grid);

        _fldItems = AddField("Items (one per line)", multiline: true);
        _fldBack = AddField("Back color (#rrggbb)");
        _fldFore = AddField("Fore color (#rrggbb)");

        var fgrid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        _fldFontName = MiniField(fgrid, 0, 0, "Font name");
        _fldFontSize = MiniField(fgrid, 1, 0, "Size");
        PropPanel.Children.Add(fgrid);

        _chkPassword = AddCheck("Password field");
        _chkChecked = AddCheck("Checked / selected");
        _chkOnClick = AddCheck("Generate OnClick handler");
        _chkOnChange = AddCheck("Generate OnChange handler");
        _chkOnDbl = AddCheck("Generate OnDoubleClick handler");

        _fldName.TextChanged += (_, _) => { if (Ready()) _selected!.EnumName = _fldName.Text ?? ""; };
        _fldText.TextChanged += (_, _) => { if (Ready()) { _selected!.Text = _fldText.Text ?? ""; Refresh(_selected); } };
        _fldItems.TextChanged += (_, _) => { if (Ready()) { _selected!.Items = _fldItems.Text ?? ""; Refresh(_selected); } };
        _fldX.TextChanged += (_, _) => ApplyBounds();
        _fldY.TextChanged += (_, _) => ApplyBounds();
        _fldW.TextChanged += (_, _) => ApplyBounds();
        _fldH.TextChanged += (_, _) => ApplyBounds();
        _fldBack.TextChanged += (_, _) => { if (Ready()) { _selected!.BackColor = ParseColor(_fldBack.Text); Refresh(_selected); } };
        _fldFore.TextChanged += (_, _) => { if (Ready()) { _selected!.ForeColor = ParseColor(_fldFore.Text); Refresh(_selected); } };
        _fldFontName.TextChanged += (_, _) => { if (Ready()) _selected!.FontName = _fldFontName.Text ?? ""; };
        _fldFontSize.TextChanged += (_, _) => { if (Ready()) _selected!.FontSize = ParseInt(_fldFontSize.Text, 0); };
        _chkPassword.IsCheckedChanged += (_, _) => { if (Ready()) _selected!.IsPassword = _chkPassword.IsChecked == true; };
        _chkChecked.IsCheckedChanged += (_, _) => { if (Ready()) _selected!.IsChecked = _chkChecked.IsChecked == true; };
        _chkOnClick.IsCheckedChanged += (_, _) => { if (Ready()) _selected!.OnClickEvent = _chkOnClick.IsChecked == true; };
        _chkOnChange.IsCheckedChanged += (_, _) => { if (Ready()) _selected!.OnChangeEvent = _chkOnChange.IsChecked == true; };
        _chkOnDbl.IsCheckedChanged += (_, _) => { if (Ready()) _selected!.OnDoubleClickEvent = _chkOnDbl.IsChecked == true; };
    }

    private bool Ready() => !_suppress && _selected != null;

    private TextBox AddField(string label, bool multiline = false)
    {
        PropPanel.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.Parse("#AAA")), FontSize = 11 });
        var tb = new TextBox { AcceptsReturn = multiline, Height = multiline ? 90 : double.NaN, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        PropPanel.Children.Add(tb);
        return tb;
    }

    private CheckBox AddCheck(string label)
    {
        var cb = new CheckBox { Content = label, Foreground = new SolidColorBrush(Color.Parse("#CCC")), FontSize = 12 };
        PropPanel.Children.Add(cb);
        return cb;
    }

    private static TextBox MiniField(Grid grid, int col, int row, string label)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 4) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.Parse("#AAA")), FontSize = 11 });
        var tb = new TextBox();
        panel.Children.Add(tb);
        Grid.SetColumn(panel, col);
        Grid.SetRow(panel, row);
        grid.Children.Add(panel);
        return tb;
    }

    private void ApplyBounds()
    {
        if (!Ready()) return;
        _selected!.X = ParseInt(_fldX.Text, _selected.X);
        _selected.Y = ParseInt(_fldY.Text, _selected.Y);
        _selected.W = ParseInt(_fldW.Text, _selected.W);
        _selected.H = ParseInt(_fldH.Text, _selected.H);
        if (_controls.TryGetValue(_selected, out var b))
        {
            Canvas.SetLeft(b, _selected.X);
            Canvas.SetTop(b, _selected.Y);
            b.Width = _selected.W;
            b.Height = _selected.H;
        }
        PositionHandles();
    }

    private void UpdatePropFields()
    {
        _suppress = true;
        if (_selected is null)
        {
            _fldName.Text = _fldText.Text = _fldItems.Text = "";
            _fldX.Text = _fldY.Text = _fldW.Text = _fldH.Text = "";
            _fldBack.Text = _fldFore.Text = _fldFontName.Text = _fldFontSize.Text = "";
            _chkPassword.IsChecked = _chkChecked.IsChecked = false;
            _chkOnClick.IsChecked = _chkOnChange.IsChecked = _chkOnDbl.IsChecked = false;
            PropPanel.IsEnabled = false;
        }
        else
        {
            PropPanel.IsEnabled = true;
            _fldName.Text = _selected.EnumName;
            _fldText.Text = _selected.Text;
            _fldItems.Text = _selected.Items;
            _fldX.Text = _selected.X.ToString();
            _fldY.Text = _selected.Y.ToString();
            _fldW.Text = _selected.W.ToString();
            _fldH.Text = _selected.H.ToString();
            _fldBack.Text = ColorToHex(_selected.BackColor);
            _fldFore.Text = ColorToHex(_selected.ForeColor);
            _fldFontName.Text = _selected.FontName;
            _fldFontSize.Text = _selected.FontSize > 0 ? _selected.FontSize.ToString() : "";
            _chkPassword.IsChecked = _selected.IsPassword;
            _chkChecked.IsChecked = _selected.IsChecked;
            _chkOnClick.IsChecked = _selected.OnClickEvent;
            _chkOnChange.IsChecked = _selected.OnChangeEvent;
            _chkOnDbl.IsChecked = _selected.OnDoubleClickEvent;
        }
        _suppress = false;
    }

    private void Refresh(W9GadgetInstance g)
    {
        if (_controls.TryGetValue(g, out var b)) ApplyVisual(g, b);
    }

    private void ApplyVisual(W9GadgetInstance g, Border b)
    {
        b.Background = g.BackColor.IsEmpty
            ? BgFor(g.GadgetType)
            : new SolidColorBrush(Color.FromArgb(255, g.BackColor.R, g.BackColor.G, g.BackColor.B));
        if (b.Child is TextBlock tb)
        {
            tb.Text = CaptionFor(g);
            tb.Foreground = g.ForeColor.IsEmpty
                ? Brushes.Black
                : new SolidColorBrush(Color.FromArgb(255, g.ForeColor.R, g.ForeColor.G, g.ForeColor.B));
        }
    }

    // ---- Generate ----

    private void Generate()
    {
        _design.FormTitle = FormTitleBox.Text ?? "Form";
        var code = W9CodeGenerator.GenerateCode(_design);
        _onGenerate?.Invoke(code);
        DesignerStatus.Text = $"Generated {_design.Gadgets.Count} gadget(s) → editor";
    }

    // ---- Helpers ----

    private void ResizeCanvas()
    {
        DesignCanvas.Width = Math.Max(80, _design.FormWidth);
        DesignCanvas.Height = Math.Max(60, _design.FormHeight);
    }

    private static int ParseInt(string? s, int fallback) =>
        int.TryParse((s ?? "").Trim(), out var v) ? v : fallback;

    private static System.Drawing.Color ParseColor(string? s)
    {
        s = (s ?? "").Trim().TrimStart('#');
        if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return System.Drawing.Color.FromArgb(255, (v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        return System.Drawing.Color.Empty;
    }

    private static string ColorToHex(System.Drawing.Color c) =>
        c.IsEmpty ? "" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static string CaptionFor(W9GadgetInstance g)
    {
        switch (g.GadgetType)
        {
            case W9GadgetType.ListBox:
            case W9GadgetType.ComboBox:
                var first = (g.Items ?? "").Split('\n');
                return first.Length > 0 && first[0].Length > 0 ? first[0] + " ▾" : "(list)";
            case W9GadgetType.StringInput:
                return g.IsPassword ? "••••••" : (string.IsNullOrEmpty(g.Text) ? "" : g.Text);
            case W9GadgetType.CheckBox:
                return (g.IsChecked ? "☑ " : "☐ ") + g.Text;
            case W9GadgetType.OptionButton:
                return (g.IsChecked ? "◉ " : "○ ") + g.Text;
            default:
                return string.IsNullOrEmpty(g.Text) ? $"[{g.GadgetType}]" : g.Text;
        }
    }

    private static IBrush BgFor(W9GadgetType gt) => gt switch
    {
        W9GadgetType.Button => new SolidColorBrush(Color.Parse("#DDDDDD")),
        W9GadgetType.TextLabel => Brushes.Transparent,
        W9GadgetType.StringInput or W9GadgetType.Editor or W9GadgetType.ListBox
            or W9GadgetType.ComboBox or W9GadgetType.TreeView or W9GadgetType.ListView
            => Brushes.White,
        W9GadgetType.CheckBox or W9GadgetType.OptionButton or W9GadgetType.GroupBox
            => Brushes.Transparent,
        _ => new SolidColorBrush(Color.Parse("#E8E8E8"))
    };
}
