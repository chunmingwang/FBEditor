using System.Runtime.InteropServices;

namespace FBEditor.Core;

/// <summary>
/// Phase 0 stub. This gets deleted once real engine modules
/// (W9CodeGenerator, W9GadgetInfo, CodeOutline, BuildSystem, GDBDebugger, ...)
/// are ported in. It exists only to prove that FBEditor.Avalonia can reference
/// and execute FBEditor.Core on Linux.
/// </summary>
public static class CoreInfo
{
    public const string Version = "5.3.0-port";

    public static string Banner() =>
        $"""
        FBEditor.Core {Version}  (stub)

        .NET runtime : {RuntimeInformation.FrameworkDescription}
        OS           : {RuntimeInformation.OSDescription}
        Arch         : {RuntimeInformation.OSArchitecture}

        Core is referenced and executing.
        If you can read this in the window, Phase 0 passed.
        """;
}
