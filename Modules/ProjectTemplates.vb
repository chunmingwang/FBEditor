''' <summary>
''' Built-in project templates for File > New from Template.
''' Each template provides a name, description, default filename, and source code.
''' </summary>
Public Module ProjectTemplates

    Public Class Template
        Public Name As String
        Public Description As String
        Public DefaultFileName As String
        Public Code As String
    End Class

    Public ReadOnly Templates As Template() = {
        New Template() With {
            .Name = "Console Hello World",
            .Description = "Simple console application that prints Hello World.",
            .DefaultFileName = "hello.bas",
            .Code =
"' Console Hello World" & vbCrLf &
"' Created with FBEditor" & vbCrLf &
"" & vbCrLf &
"Print ""Hello, World!""" & vbCrLf &
"Print" & vbCrLf &
"Print ""Press any key to exit...""" & vbCrLf &
"Sleep" & vbCrLf
        },
        New Template() With {
            .Name = "Window9 Basic Form",
            .Description = "Basic Window9 GUI application with a single form.",
            .DefaultFileName = "basicform.bas",
            .Code =
"' Window9 Basic Form" & vbCrLf &
"' Created with FBEditor" & vbCrLf &
"" & vbCrLf &
"#Include Once ""window9.bi""" & vbCrLf &
"" & vbCrLf &
"Dim As Long hWin" & vbCrLf &
"" & vbCrLf &
"hWin = Window9(""My Application"", 100, 100, 640, 480)" & vbCrLf &
"" & vbCrLf &
"' Main message loop" & vbCrLf &
"Do" & vbCrLf &
"    Dim As Long msg, wParam, lParam" & vbCrLf &
"    msg = GetMessage9(hWin, wParam, lParam)" & vbCrLf &
"    " & vbCrLf &
"    Select Case msg" & vbCrLf &
"        Case WM9_CLOSE" & vbCrLf &
"            Exit Do" & vbCrLf &
"    End Select" & vbCrLf &
"Loop" & vbCrLf &
"" & vbCrLf &
"CloseWindow9(hWin)" & vbCrLf
        },
        New Template() With {
            .Name = "Window9 Dialog",
            .Description = "Simple dialog-style Window9 form with OK and Cancel buttons.",
            .DefaultFileName = "dialog.bas",
            .Code =
"' Window9 Dialog" & vbCrLf &
"' Created with FBEditor" & vbCrLf &
"" & vbCrLf &
"#Include Once ""window9.bi""" & vbCrLf &
"" & vbCrLf &
"Dim As Long hWin, btnOK, btnCancel, lblMessage" & vbCrLf &
"" & vbCrLf &
"hWin = Window9(""My Dialog"", 200, 200, 400, 200)" & vbCrLf &
"lblMessage = Label9(hWin, ""Enter your information below:"", 20, 20, 360, 24)" & vbCrLf &
"btnOK = Button9(hWin, ""OK"", 120, 120, 80, 30)" & vbCrLf &
"btnCancel = Button9(hWin, ""Cancel"", 210, 120, 80, 30)" & vbCrLf &
"" & vbCrLf &
"' Main message loop" & vbCrLf &
"Do" & vbCrLf &
"    Dim As Long msg, wParam, lParam" & vbCrLf &
"    msg = GetMessage9(hWin, wParam, lParam)" & vbCrLf &
"    " & vbCrLf &
"    Select Case msg" & vbCrLf &
"        Case WM9_CLOSE" & vbCrLf &
"            Exit Do" & vbCrLf &
"        Case WM9_COMMAND" & vbCrLf &
"            If wParam = btnOK Then" & vbCrLf &
"                ' Handle OK button click" & vbCrLf &
"                Exit Do" & vbCrLf &
"            ElseIf wParam = btnCancel Then" & vbCrLf &
"                Exit Do" & vbCrLf &
"            End If" & vbCrLf &
"    End Select" & vbCrLf &
"Loop" & vbCrLf &
"" & vbCrLf &
"CloseWindow9(hWin)" & vbCrLf
        },
        New Template() With {
            .Name = "Window9 Multi-Form",
            .Description = "Window9 application with a main form that opens a child form.",
            .DefaultFileName = "multiform.bas",
            .Code =
"' Window9 Multi-Form Application" & vbCrLf &
"' Created with FBEditor" & vbCrLf &
"" & vbCrLf &
"#Include Once ""window9.bi""" & vbCrLf &
"" & vbCrLf &
"Dim Shared As Long hMain, hChild" & vbCrLf &
"Dim Shared As Long btnOpenChild, btnCloseChild" & vbCrLf &
"" & vbCrLf &
"' Create main form" & vbCrLf &
"hMain = Window9(""Main Form"", 100, 100, 640, 480)" & vbCrLf &
"btnOpenChild = Button9(hMain, ""Open Child Form"", 20, 20, 160, 30)" & vbCrLf &
"" & vbCrLf &
"' Main message loop" & vbCrLf &
"Do" & vbCrLf &
"    Dim As Long msg, wParam, lParam" & vbCrLf &
"    msg = GetMessage9(hMain, wParam, lParam)" & vbCrLf &
"    " & vbCrLf &
"    Select Case msg" & vbCrLf &
"        Case WM9_CLOSE" & vbCrLf &
"            Exit Do" & vbCrLf &
"        Case WM9_COMMAND" & vbCrLf &
"            If wParam = btnOpenChild Then" & vbCrLf &
"                ' Create and show child form" & vbCrLf &
"                hChild = Window9(""Child Form"", 200, 200, 400, 300)" & vbCrLf &
"                btnCloseChild = Button9(hChild, ""Close"", 150, 220, 100, 30)" & vbCrLf &
"            ElseIf wParam = btnCloseChild Then" & vbCrLf &
"                CloseWindow9(hChild)" & vbCrLf &
"                hChild = 0" & vbCrLf &
"            End If" & vbCrLf &
"    End Select" & vbCrLf &
"Loop" & vbCrLf &
"" & vbCrLf &
"If hChild <> 0 Then CloseWindow9(hChild)" & vbCrLf &
"CloseWindow9(hMain)" & vbCrLf
        },
        New Template() With {
            .Name = "Class Module",
            .Description = "FreeBASIC Type (class) with constructor, destructor, and example methods.",
            .DefaultFileName = "myclass.bas",
            .Code =
"' Class Module Example" & vbCrLf &
"' Created with FBEditor" & vbCrLf &
"" & vbCrLf &
"Type MyClass" & vbCrLf &
"    Private:" & vbCrLf &
"        _name As String" & vbCrLf &
"        _value As Integer" & vbCrLf &
"    " & vbCrLf &
"    Public:" & vbCrLf &
"        Declare Constructor()" & vbCrLf &
"        Declare Constructor(name As String, value As Integer)" & vbCrLf &
"        Declare Destructor()" & vbCrLf &
"        Declare Property Name() As String" & vbCrLf &
"        Declare Property Name(value As String)" & vbCrLf &
"        Declare Property Value() As Integer" & vbCrLf &
"        Declare Property Value(v As Integer)" & vbCrLf &
"        Declare Sub PrintInfo()" & vbCrLf &
"End Type" & vbCrLf &
"" & vbCrLf &
"Constructor MyClass()" & vbCrLf &
"    _name = ""Unnamed""" & vbCrLf &
"    _value = 0" & vbCrLf &
"End Constructor" & vbCrLf &
"" & vbCrLf &
"Constructor MyClass(name As String, value As Integer)" & vbCrLf &
"    _name = name" & vbCrLf &
"    _value = value" & vbCrLf &
"End Constructor" & vbCrLf &
"" & vbCrLf &
"Destructor MyClass()" & vbCrLf &
"    ' Clean up resources here" & vbCrLf &
"End Destructor" & vbCrLf &
"" & vbCrLf &
"Property MyClass.Name() As String" & vbCrLf &
"    Return _name" & vbCrLf &
"End Property" & vbCrLf &
"" & vbCrLf &
"Property MyClass.Name(value As String)" & vbCrLf &
"    _name = value" & vbCrLf &
"End Property" & vbCrLf &
"" & vbCrLf &
"Property MyClass.Value() As Integer" & vbCrLf &
"    Return _value" & vbCrLf &
"End Property" & vbCrLf &
"" & vbCrLf &
"Property MyClass.Value(v As Integer)" & vbCrLf &
"    _value = v" & vbCrLf &
"End Property" & vbCrLf &
"" & vbCrLf &
"Sub MyClass.PrintInfo()" & vbCrLf &
"    Print ""Name: "" & _name & "", Value: "" & Str(_value)" & vbCrLf &
"End Sub" & vbCrLf &
"" & vbCrLf &
"' --- Test ---" & vbCrLf &
"Dim obj As MyClass = MyClass(""Test"", 42)" & vbCrLf &
"obj.PrintInfo()" & vbCrLf &
"" & vbCrLf &
"Sleep" & vbCrLf
        }
    }

End Module
