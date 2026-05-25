using System;
using Avalonia;

namespace FBEditor.Avalonia;

internal static class Program
{
    // Avalonia configuration; don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()   // picks X11 on your Devuan/Xorg setup
            .WithInterFont()       // bundles a font so text renders consistently across distros
            .LogToTrace();
}
