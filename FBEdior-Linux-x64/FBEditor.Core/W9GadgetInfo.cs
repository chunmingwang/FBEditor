using System.Drawing; // Primitives only: Color, Point, Rectangle. NEVER GDI+ (Graphics/Bitmap/Font).
using Newtonsoft.Json;

namespace FBEditor.Core;

// Data model for the Window9 visual form designer.
// Contains definitions for all supported gadgets, their properties,
// and the form/project model used by the designer and code generator.
//
// Ported from Modules/W9GadgetInfo.vb. Public *fields* are kept as fields
// (not auto-properties) on purpose: Newtonsoft serializes public fields by
// default, so the .w9form JSON key names stay identical to the VB version.

/// <summary>Project types supported by FBEditor.</summary>
public enum ProjectType
{
    ConsoleApp = 0,
    GUIApp = 1,          // -s gui (no Window9)
    Window9FormsApp = 2  // -s gui + Window9
}

/// <summary>Available Window9 gadget types.</summary>
public enum W9GadgetType
{
    Button = 0,
    TextLabel = 1,
    Editor = 2,
    StringInput = 3,
    CheckBox = 4,
    OptionButton = 5,
    ComboBox = 6,
    ListBox = 7,
    GroupBox = 8,
    ImageBox = 9,
    ProgressBar = 10,
    ScrollBar = 11,
    TrackBar = 12,
    SpinBox = 13,
    TreeView = 14,
    ListView = 15,
    StatusBar = 16,
    PanelTab = 17,
    Container = 18,
    Splitter = 19,
    Calendar = 20,
    HyperLink = 21,
    WebBrowser = 22
}

/// <summary>Form type — main window or child/dialog window.</summary>
public enum W9FormType
{
    MainForm = 0,
    ChildForm = 1
}

/// <summary>Metadata about each gadget type for the toolbox and designer.</summary>
public class W9GadgetTypeDef
{
    public W9GadgetType GadgetType;
    public string DisplayName = "";
    public string W9FunctionName = "";
    public int DefaultWidth = 100;
    public int DefaultHeight = 30;
    public string DefaultText = "";
    public string ToolboxCategory = "Common Controls";
    public string ToolboxIcon = "";
    public bool SupportsText = true;
    public bool SupportsColor = true;
    public bool SupportsFont = true;
    public bool SupportsResize = true;
    public bool IsContainer = false;
    public string HasDefaultStyle = "";
    public string ExtraParams = "";
}

/// <summary>Instance of a gadget placed on the designer canvas.</summary>
public class W9GadgetInstance
{
    public int ID = 0;
    public W9GadgetType GadgetType = W9GadgetType.Button;
    public string Name = "";        // Variable name prefix (e.g. "giButton1")
    public string EnumName = "";    // Enum member name
    public string Text = "";
    public int X = 10;
    public int Y = 10;
    public int W = 100;
    public int H = 30;
    public string Style = "";
    public string ExStyle = "";
    public string FontName = "";
    public int FontSize = 0;
    public bool IsReadOnly = false;
    public bool WordWrap = false;
    public Color BackColor = Color.Empty;
    public Color ForeColor = Color.Empty;
    public string Tag = "";
    public int ZOrder = 0;

    // Designer-only state
    public bool IsSelected = false;
    public bool IsLocked = false;

    // Container nesting: 0 = directly on form, >0 = ID of parent container gadget
    public int ParentContainerID = 0;

    // Anchor for resize behavior: "TopLeft" (default), "TopRight", "BottomLeft", "BottomRight",
    // "TopLeftRight", "LeftTopBottom", "RightTopBottom", "BottomLeftRight", "All"
    public string Anchor = "TopLeft";

    // Scrollbar / Trackbar / Spin specifics
    public int MinValue = 0;
    public int MaxValue = 100;
    public int CurrentValue = 0;
    public int Orientation = 0;    // 0=Horiz, 1=Vert

    // StatusBar fields
    public List<StatusBarFieldInfo> StatusBarFields = new();

    // ListView columns
    public List<string> ListViewColumns = new();

    // ComboBox / ListBox initial items (one per line)
    public string Items = "";

    // CheckBox / OptionButton initial state
    public bool IsChecked = false;

    // StringInput password mode
    public bool IsPassword = false;

    // Tooltip text
    public string Tooltip = "";

    // Enabled state (False = DisableGadget at creation)
    public bool IsEnabled = true;

    // Visible state (False = HideGadget at creation)
    public bool IsVisible = true;

    // ImageBox / ButtonImage: image file path
    public string ImagePath = "";

    // PanelTab: tab names (one per line)
    public string TabNames = "";

    // Editor: multiline with scrollbar flags
    public bool HasVScroll = true;
    public bool HasHScroll = false;

    // Events - which event handlers to generate
    public bool OnClickEvent = false;
    public bool OnChangeEvent = false;
    public bool OnDoubleClickEvent = false;

    // TreeView / ListView style options
    public bool HasLines = true;        // TVS_HASLINES
    public bool HasButtons = true;      // TVS_HASBUTTONS
    public bool HasCheckBoxes = false;  // TVS_CHECKBOXES / LVS_EX_CHECKBOXES
    public bool FullRowSelect = false;  // LVS_EX_FULLROWSELECT

    /// <summary>Get the display bounds for painting on the canvas.</summary>
    [JsonIgnore]
    public Rectangle Bounds => new Rectangle(X, Y, W, H);

    public W9GadgetInstance Clone()
    {
        var c = new W9GadgetInstance
        {
            ID = ID, GadgetType = GadgetType, Name = Name, EnumName = EnumName,
            Text = Text, X = X, Y = Y, W = W, H = H,
            Style = Style, ExStyle = ExStyle, FontName = FontName, FontSize = FontSize,
            IsReadOnly = IsReadOnly, WordWrap = WordWrap,
            BackColor = BackColor, ForeColor = ForeColor, Tag = Tag, ZOrder = ZOrder,
            MinValue = MinValue, MaxValue = MaxValue, CurrentValue = CurrentValue,
            Orientation = Orientation, IsLocked = IsLocked,
            Items = Items, IsChecked = IsChecked, IsPassword = IsPassword,
            Tooltip = Tooltip, IsEnabled = IsEnabled, IsVisible = IsVisible,
            ImagePath = ImagePath, TabNames = TabNames,
            HasVScroll = HasVScroll, HasHScroll = HasHScroll,
            OnClickEvent = OnClickEvent, OnChangeEvent = OnChangeEvent,
            OnDoubleClickEvent = OnDoubleClickEvent,
            HasLines = HasLines, HasButtons = HasButtons,
            HasCheckBoxes = HasCheckBoxes, FullRowSelect = FullRowSelect,
            ParentContainerID = ParentContainerID, Anchor = Anchor
        };
        // NOTE (behavior preserved from VB): the original Clone() does NOT copy
        // StatusBarFields or ListViewColumns, so a clone gets fresh empty lists.
        // Faithfully kept. If you want deep-copy semantics, this is the spot to fix it.
        return c;
    }
}

/// <summary>Status bar field definition.</summary>
public class StatusBarFieldInfo
{
    public int Width = -1;
    public string Text = "";
}

/// <summary>Menu item for the menu designer.</summary>
public class W9MenuItemInfo
{
    public int ID = 0;
    public string EnumName = "";
    public string Text = "";
    public bool IsSeparator = false;
    public List<W9MenuItemInfo> Children = new();
    public bool IsTopLevel = false;
}

/// <summary>
/// Complete form design model — represents one Window9 form with all its gadgets and menus.
/// This is what gets serialized/deserialized and used by the code generator.
/// </summary>
public class W9FormDesign
{
    public string FormTitle = "My Window9 Application";
    public int FormX = 100;
    public int FormY = 50;
    public int FormWidth = 800;
    public int FormHeight = 600;
    public bool CenterOnScreen = true;
    public Color FormColor = Color.Empty;
    public int BaseWidth = 800;
    public int BaseHeight = 600;
    public bool ProportionalResize = true;

    // Multi-form support
    public W9FormType FormType = W9FormType.MainForm;
    public string VarName = "hMainForm";
    public bool HideOnClose = false;   // True = hidewindow on close instead of End
    public bool StartHidden = false;   // True = hide immediately after creation

    public List<W9GadgetInstance> Gadgets = new();
    public List<W9MenuItemInfo> MenuItems = new();

    // Event loop options
    public bool HandleResize = true;
    public bool HasTimer = false;
    public int TimerInterval = 1000;
    public bool HasKeyboardShortcut = false;
    public int DefaultButtonID = 0;

    // Enum start counters
    public int GadgetEnumStart = 100;
    public int MenuEnumStart = 200;
    public int ShortcutEnumStart = 1000;

    private int _nextGadgetId = 101;
    private int _nextMenuId = 201;

    public int GetNextGadgetID()
    {
        _nextGadgetId += 1;
        return _nextGadgetId - 1;
    }

    public int GetNextMenuID()
    {
        _nextMenuId += 1;
        return _nextMenuId - 1;
    }

    /// <summary>Remove a gadget by reference.</summary>
    public void RemoveGadget(W9GadgetInstance g) => Gadgets.Remove(g);

    /// <summary>Find the topmost gadget at a given point.</summary>
    public W9GadgetInstance? HitTest(Point pt)
    {
        // Search in reverse Z-order (topmost first)
        for (int i = Gadgets.Count - 1; i >= 0; i--)
        {
            if (Gadgets[i].Bounds.Contains(pt)) return Gadgets[i];
        }
        return null;
    }

    /// <summary>Clear selection on all gadgets.</summary>
    public void ClearSelection()
    {
        foreach (var g in Gadgets)
            g.IsSelected = false;
    }
}

/// <summary>
/// A multi-form project containing one or more W9FormDesign instances.
/// The first form is always the main form. Additional forms are child/dialog forms.
/// All gadget IDs are unique across the entire project.
/// </summary>
public class W9FormProject
{
    public List<W9FormDesign> Forms = new();

    public W9FormProject()
    {
        // Start with a default main form
        var mainForm = new W9FormDesign
        {
            FormType = W9FormType.MainForm,
            VarName = "hMainForm",
            FormTitle = "My Window9 Application"
        };
        Forms.Add(mainForm);
    }

    /// <summary>The main (first) form. Not serialized — computed from Forms list.</summary>
    [JsonIgnore]
    public W9FormDesign? MainForm => Forms.Count == 0 ? null : Forms[0];

    /// <summary>Add a new child form with a unique variable name.</summary>
    public W9FormDesign AddChildForm(string title)
    {
        int idx = Forms.Count;
        string varName = "hForm" + idx;
        // Make sure var name is unique
        while (Forms.Any(f => f.VarName == varName))
        {
            idx += 1;
            varName = "hForm" + idx;
        }

        var child = new W9FormDesign
        {
            FormType = W9FormType.ChildForm,
            VarName = varName,
            FormTitle = title,
            FormWidth = 400,
            FormHeight = 300,
            BaseWidth = 400,
            BaseHeight = 300,
            HideOnClose = true,
            StartHidden = true,
            CenterOnScreen = false,
            HasKeyboardShortcut = false
        };
        // Offset gadget IDs so they don't clash
        child.GadgetEnumStart = GetNextAvailableEnumStart();
        Forms.Add(child);
        return child;
    }

    /// <summary>Remove a child form (cannot remove the main form).</summary>
    public bool RemoveForm(W9FormDesign form)
    {
        if (form is null || form.FormType == W9FormType.MainForm) return false;
        return Forms.Remove(form);
    }

    /// <summary>Get the next available gadget ID across all forms.</summary>
    public int GetNextGlobalGadgetID()
    {
        int maxId = 100;
        foreach (var f in Forms)
            foreach (var g in f.Gadgets)
                if (g.ID > maxId) maxId = g.ID;
        return maxId + 1;
    }

    /// <summary>Get a safe enum start value that won't overlap.</summary>
    private int GetNextAvailableEnumStart()
    {
        int maxStart = 100;
        foreach (var f in Forms)
        {
            int formMax = f.GadgetEnumStart;
            foreach (var g in f.Gadgets)
                if (g.ID >= formMax) formMax = g.ID + 1;
            if (formMax > maxStart) maxStart = formMax;
        }
        // Round up to next 100
        return (int)(Math.Ceiling(maxStart / 100.0) * 100);
    }

    /// <summary>Get all gadgets across all forms (for unified enum).</summary>
    public List<W9GadgetInstance> AllGadgets()
    {
        var all = new List<W9GadgetInstance>();
        foreach (var f in Forms)
            all.AddRange(f.Gadgets);
        return all;
    }
}

/// <summary>
/// Registry of all Window9 gadget type definitions.
/// Used by the toolbox and code generator.
/// (VB Module -> C# static class.)
/// </summary>
public static class W9GadgetRegistry
{
    private static List<W9GadgetTypeDef>? _types;

    public static List<W9GadgetTypeDef> AllTypes
    {
        get
        {
            if (_types is null) BuildRegistry();
            return _types!;
        }
    }

    public static W9GadgetTypeDef? GetTypeDef(W9GadgetType gt) =>
        AllTypes.Find(t => t.GadgetType == gt);

    private static void BuildRegistry()
    {
        _types = new List<W9GadgetTypeDef>
        {
            new() { GadgetType = W9GadgetType.Button, DisplayName = "Button",
                W9FunctionName = "ButtonGadget", DefaultWidth = 100, DefaultHeight = 30,
                DefaultText = "Button", ToolboxIcon = "B", ToolboxCategory = "Common Controls",
                HasDefaultStyle = "BS_DEFPUSHBUTTON" },
            new() { GadgetType = W9GadgetType.TextLabel, DisplayName = "Text Label",
                W9FunctionName = "TextGadget", DefaultWidth = 120, DefaultHeight = 22,
                DefaultText = "Label", ToolboxIcon = "T", ToolboxCategory = "Common Controls",
                HasDefaultStyle = "SS_NOTIFY" },
            new() { GadgetType = W9GadgetType.Editor, DisplayName = "Editor (Multiline)",
                W9FunctionName = "EditorGadget", DefaultWidth = 300, DefaultHeight = 200,
                DefaultText = "", ToolboxIcon = "Ed", ToolboxCategory = "Common Controls" },
            new() { GadgetType = W9GadgetType.StringInput, DisplayName = "String Input",
                W9FunctionName = "StringGadget", DefaultWidth = 200, DefaultHeight = 24,
                DefaultText = "", ToolboxIcon = "S", ToolboxCategory = "Common Controls" },
            new() { GadgetType = W9GadgetType.CheckBox, DisplayName = "CheckBox",
                W9FunctionName = "CheckBoxGadget", DefaultWidth = 130, DefaultHeight = 24,
                DefaultText = "CheckBox", ToolboxIcon = "Ch", ToolboxCategory = "Common Controls" },
            new() { GadgetType = W9GadgetType.OptionButton, DisplayName = "Option (Radio)",
                W9FunctionName = "OptionGadget", DefaultWidth = 130, DefaultHeight = 24,
                DefaultText = "Option", ToolboxIcon = "O", ToolboxCategory = "Common Controls" },
            new() { GadgetType = W9GadgetType.ComboBox, DisplayName = "ComboBox",
                W9FunctionName = "ComboBoxGadget", DefaultWidth = 180, DefaultHeight = 24,
                DefaultText = "", ToolboxIcon = "Cb", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.ListBox, DisplayName = "ListBox",
                W9FunctionName = "ListBoxGadget", DefaultWidth = 180, DefaultHeight = 120,
                DefaultText = "", ToolboxIcon = "Lb", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.GroupBox, DisplayName = "GroupBox",
                W9FunctionName = "GroupGadget", DefaultWidth = 200, DefaultHeight = 150,
                DefaultText = "Group", ToolboxIcon = "G", ToolboxCategory = "Containers",
                IsContainer = true },
            new() { GadgetType = W9GadgetType.ImageBox, DisplayName = "Image",
                W9FunctionName = "ImageGadget", DefaultWidth = 100, DefaultHeight = 100,
                DefaultText = "", ToolboxIcon = "Im", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.ProgressBar, DisplayName = "ProgressBar",
                W9FunctionName = "ProgressBarGadget", DefaultWidth = 200, DefaultHeight = 24,
                DefaultText = "", ToolboxIcon = "Pb", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.ScrollBar, DisplayName = "ScrollBar",
                W9FunctionName = "ScrollBarGadget", DefaultWidth = 200, DefaultHeight = 20,
                DefaultText = "", ToolboxIcon = "Sc", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.TrackBar, DisplayName = "TrackBar (Slider)",
                W9FunctionName = "TrackBarGadget", DefaultWidth = 200, DefaultHeight = 40,
                DefaultText = "", ToolboxIcon = "Tk", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.SpinBox, DisplayName = "Spin",
                W9FunctionName = "SpinGadget", DefaultWidth = 80, DefaultHeight = 24,
                DefaultText = "", ToolboxIcon = "Sp", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.TreeView, DisplayName = "TreeView",
                W9FunctionName = "TreeViewGadget", DefaultWidth = 200, DefaultHeight = 200,
                DefaultText = "", ToolboxIcon = "Tv", ToolboxCategory = "Data Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.ListView, DisplayName = "ListView",
                W9FunctionName = "ListViewGadget", DefaultWidth = 300, DefaultHeight = 200,
                DefaultText = "", ToolboxIcon = "Lv", ToolboxCategory = "Data Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.StatusBar, DisplayName = "StatusBar",
                W9FunctionName = "StatusBarGadget", DefaultWidth = 0, DefaultHeight = 24,
                DefaultText = "", ToolboxIcon = "Sb", ToolboxCategory = "Common Controls",
                SupportsResize = false },
            new() { GadgetType = W9GadgetType.PanelTab, DisplayName = "Panel (Tabs)",
                W9FunctionName = "PanelGadget", DefaultWidth = 300, DefaultHeight = 200,
                DefaultText = "", ToolboxIcon = "Pn", ToolboxCategory = "Containers",
                SupportsText = false, IsContainer = true },
            new() { GadgetType = W9GadgetType.Container, DisplayName = "Container",
                W9FunctionName = "ContainerGadget", DefaultWidth = 250, DefaultHeight = 180,
                DefaultText = "", ToolboxIcon = "Cn", ToolboxCategory = "Containers",
                SupportsText = false, IsContainer = true },
            new() { GadgetType = W9GadgetType.Calendar, DisplayName = "Calendar",
                W9FunctionName = "CalendarGadget", DefaultWidth = 220, DefaultHeight = 180,
                DefaultText = "", ToolboxIcon = "Ca", ToolboxCategory = "Common Controls",
                SupportsText = false },
            new() { GadgetType = W9GadgetType.HyperLink, DisplayName = "HyperLink",
                W9FunctionName = "HyperLinkGadget", DefaultWidth = 150, DefaultHeight = 22,
                DefaultText = "Click here", ToolboxIcon = "Hl", ToolboxCategory = "Common Controls" }
            // NOTE: matches the VB registry exactly — Splitter and WebBrowser are
            // intentionally absent here (they exist in the enum but have no toolbox def).
        };
    }

    /// <summary>Generate a default enum name for a gadget instance.</summary>
    public static string GenerateEnumName(W9GadgetType gt, int index) => gt switch
    {
        W9GadgetType.Button => "giButton" + index,
        W9GadgetType.TextLabel => "giLabel" + index,
        W9GadgetType.Editor => "giEditor" + index,
        W9GadgetType.StringInput => "giString" + index,
        W9GadgetType.CheckBox => "giCheckBox" + index,
        W9GadgetType.OptionButton => "giOption" + index,
        W9GadgetType.ComboBox => "giComboBox" + index,
        W9GadgetType.ListBox => "giListBox" + index,
        W9GadgetType.GroupBox => "giGroup" + index,
        W9GadgetType.ImageBox => "giImage" + index,
        W9GadgetType.ProgressBar => "giProgressBar" + index,
        W9GadgetType.ScrollBar => "giScrollBar" + index,
        W9GadgetType.TrackBar => "giTrackBar" + index,
        W9GadgetType.SpinBox => "giSpin" + index,
        W9GadgetType.TreeView => "giTreeView" + index,
        W9GadgetType.ListView => "giListView" + index,
        W9GadgetType.StatusBar => "giStatusBar" + index,
        W9GadgetType.PanelTab => "giPanel" + index,
        W9GadgetType.Container => "giContainer" + index,
        W9GadgetType.Calendar => "giCalendar" + index,
        W9GadgetType.HyperLink => "giHyperLink" + index,
        _ => "giGadget" + index
    };

    /// <summary>Generate a unique enum name that doesn't conflict with any existing names across all forms.</summary>
    public static string GenerateUniqueEnumName(W9GadgetType gt, HashSet<string> existingNames)
    {
        int i = 1;
        while (true)
        {
            string candidate = GenerateEnumName(gt, i);
            if (!existingNames.Contains(candidate)) return candidate;
            i += 1;
        }
    }
}
