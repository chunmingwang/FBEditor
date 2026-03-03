''' <summary>
''' Centralized diagnostics logger for FBEditor.
''' Replaces silent Catch blocks with logged catches so errors are diagnosable.
''' Output goes to System.Diagnostics.Debug (visible in VS Output window).
''' </summary>
Public Module DiagnosticsLogger

    Public Sub LogWarning(source As String, message As String)
        System.Diagnostics.Debug.WriteLine($"[FBEditor][{source}] WARNING: {message}")
    End Sub

    Public Sub LogError(source As String, message As String, Optional ex As Exception = Nothing)
        Dim msg = $"[FBEditor][{source}] ERROR: {message}"
        If ex IsNot Nothing Then msg &= $" | {ex.GetType().Name}: {ex.Message}"
        System.Diagnostics.Debug.WriteLine(msg)
    End Sub

    Public Sub LogInfo(source As String, message As String)
        System.Diagnostics.Debug.WriteLine($"[FBEditor][{source}] {message}")
    End Sub

End Module
