using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FBEditor.Avalonia;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        InitializeComponent();
        MnuClose.Click += (_, _) => Close();
        BtnOk.Click += (_, _) => Close();

        AddSection("File",
            ("Ctrl+N",       "New file"),
            ("Ctrl+O",       "Open file"),
            ("Ctrl+S",       "Save"),
            ("Ctrl+W",       "Close current tab"));

        AddSection("Edit",
            ("Ctrl+/",       "Toggle line comment"),
            ("Ctrl+F",       "Find / Replace in editor"),
            ("Ctrl++",       "Zoom in"),
            ("Ctrl+-",       "Zoom out"),
            ("Ctrl+0",       "Reset zoom"));

        AddSection("View",
            ("F4",           "Refresh code outline"),
            ("Ctrl+Tab",     "Next tab"),
            ("Ctrl+Shift+Tab", "Previous tab"));

        AddSection("Build",
            ("Ctrl+F5",      "Compile"),
            ("F6",           "Compile and run"));

        AddSection("Debug",
            ("F5",           "Start / Continue debugging"),
            ("Shift+F5",     "Stop debugging"),
            ("F9",           "Toggle breakpoint at caret"),
            ("F10",          "Step over"),
            ("F11",          "Step into"),
            ("Shift+F11",    "Step out"));

        AddSection("Help",
            ("F1",           "Show this Keyboard Shortcuts window"));
    }

    private void AddSection(string title, params (string key, string action)[] rows)
    {
        var header = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#4EC9B0"))
        };
        Body.Children.Add(header);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*"),
            Margin = new global::Avalonia.Thickness(8, 4, 0, 0)
        };
        for (int i = 0; i < rows.Length; i++)
            grid.RowDefinitions.Add(new RowDefinition(global::Avalonia.Controls.GridLength.Auto));

        for (int i = 0; i < rows.Length; i++)
        {
            var k = new TextBlock
            {
                Text = rows[i].key,
                FontFamily = new FontFamily("Cascadia Code,DejaVu Sans Mono,Consolas,monospace"),
                Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
                Margin = new global::Avalonia.Thickness(0, 2, 12, 2)
            };
            var a = new TextBlock
            {
                Text = rows[i].action,
                Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
                Margin = new global::Avalonia.Thickness(0, 2, 0, 2)
            };
            Grid.SetRow(k, i); Grid.SetColumn(k, 0);
            Grid.SetRow(a, i); Grid.SetColumn(a, 1);
            grid.Children.Add(k);
            grid.Children.Add(a);
        }
        Body.Children.Add(grid);
    }
}
