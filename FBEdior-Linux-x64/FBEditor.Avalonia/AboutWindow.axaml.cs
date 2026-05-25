using Avalonia.Controls;
using FBEditor.Core;

namespace FBEditor.Avalonia;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        LblTitle.Text = AppGlobals.APP_NAME;
        LblVersion.Text = "Version " + AppGlobals.APP_VERSION;
        LblAuthor.Text = "By " + AppGlobals.APP_AUTHOR;
        LblCopyright.Text = AppGlobals.APP_COPYRIGHT;
        MnuClose.Click += (_, _) => Close();
        BtnOk.Click += (_, _) => Close();
    }
}
