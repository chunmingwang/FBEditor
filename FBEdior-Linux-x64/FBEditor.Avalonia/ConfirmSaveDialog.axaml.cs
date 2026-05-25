using Avalonia.Controls;

namespace FBEditor.Avalonia;

// Order matters: Cancel = 0 so closing via the title-bar X returns Cancel (the safe default).
public enum SaveChoice { Cancel, Save, DontSave }

public partial class ConfirmSaveDialog : Window
{
    public ConfirmSaveDialog(string fileName)
    {
        InitializeComponent();
        LblMsg.Text = $"Save changes to \"{fileName}\" before closing?";
        BtnSave.Click += (_, _) => Close(SaveChoice.Save);
        BtnDont.Click += (_, _) => Close(SaveChoice.DontSave);
        BtnCancel.Click += (_, _) => Close(SaveChoice.Cancel);
    }
}
