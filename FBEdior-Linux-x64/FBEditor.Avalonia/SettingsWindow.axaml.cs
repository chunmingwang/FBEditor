using System;
using System.IO;
using Avalonia.Controls;
using FBEditor.Core;

namespace FBEditor.Avalonia;

public partial class SettingsWindow : Window
{
    private readonly Action _onApplied;
    private readonly Action<string> _onGdbPath;
    private readonly string _apiKeyPath;

    public SettingsWindow(string gdbPath, Action onApplied, Action<string> onGdbPath)
    {
        InitializeComponent();
        _onApplied = onApplied;
        _onGdbPath = onGdbPath;
        _apiKeyPath = Path.Combine(AppGlobals.SettingsPath, "api_key.txt");

        // Populate from current state.
        FbcPath.Text = AppGlobals.Build.FBCPath;
        GdbPath.Text = gdbPath;
        FontSizeBox.Text = AppGlobals.Settings.EditorFontSize.ToString();
        SpellEnabled.IsChecked = AppGlobals.Settings.SpellCheckEnabled;
        try { if (File.Exists(_apiKeyPath)) ApiKey.Text = File.ReadAllText(_apiKeyPath).Trim(); }
        catch { }

        MnuSaveSettings.Click += (_, _) => Save();
        BtnSave.Click += (_, _) => Save();
        MnuCloseSettings.Click += (_, _) => Close();
        BtnCancel.Click += (_, _) => Close();
    }

    private void Save()
    {
        AppGlobals.Build.FBCPath = (FbcPath.Text ?? "").Trim();
        if (int.TryParse((FontSizeBox.Text ?? "").Trim(), out var fs) && fs >= 6 && fs <= 48)
            AppGlobals.Settings.EditorFontSize = fs;
        AppGlobals.Settings.SpellCheckEnabled = SpellEnabled.IsChecked == true;
        AppGlobals.SaveSettings();

        _onGdbPath((GdbPath.Text ?? "").Trim());

        // API key to its own file (matches AIChatManager.LoadAPIKey lookup).
        try
        {
            var key = (ApiKey.Text ?? "").Trim();
            if (key.Length > 0)
            {
                Directory.CreateDirectory(AppGlobals.SettingsPath);
                File.WriteAllText(_apiKeyPath, key);
            }
        }
        catch (Exception ex) { StatusLine.Text = "Could not write API key: " + ex.Message; return; }

        _onApplied();
        StatusLine.Text = "Saved.";
    }
}
