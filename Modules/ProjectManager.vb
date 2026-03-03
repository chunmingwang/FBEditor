Imports System.IO
Imports Newtonsoft.Json

''' <summary>
''' Manages project settings including project type (Console/GUI/Window9),
''' Window9 library paths, and project-level configuration.
''' </summary>
Public Class ProjectSettings
    Public ProjectName As String = "Untitled"
    Public ProjectType As ProjectType = ProjectType.ConsoleApp
    Public Window9IncludePath As String = ""
    Public Window9LibPath As String = ""
    Public MainSourceFile As String = ""
    Public SourceFiles As New List(Of String)()
    Public FormDesignFile As String = ""  ' .w9form JSON file
    Public GeneratedCodeFile As String = ""  ' Auto-generated .bas from designer
    Public LastModified As DateTime = DateTime.Now
End Class

Public Module ProjectManager

    Private _currentProject As ProjectSettings = Nothing
    Private _projectFilePath As String = ""
    Private _isDirty As Boolean = False

    Public ReadOnly Property CurrentProject As ProjectSettings
        Get
            If _currentProject Is Nothing Then
                _currentProject = New ProjectSettings()
            End If
            Return _currentProject
        End Get
    End Property

    Public ReadOnly Property ProjectFilePath As String
        Get
            Return _projectFilePath
        End Get
    End Property

    Public ReadOnly Property IsDirty As Boolean
        Get
            Return _isDirty
        End Get
    End Property

    Public ReadOnly Property IsWindow9Project As Boolean
        Get
            Return CurrentProject.ProjectType = ProjectType.Window9FormsApp
        End Get
    End Property

    Public ReadOnly Property IsGUIProject As Boolean
        Get
            Return CurrentProject.ProjectType = ProjectType.GUIApp OrElse
                   CurrentProject.ProjectType = ProjectType.Window9FormsApp
        End Get
    End Property

    Public Sub MarkDirty()
        _isDirty = True
    End Sub

    ''' <summary>Create a new project with the specified type.</summary>
    Public Sub NewProject(projType As ProjectType, Optional projName As String = "Untitled")
        _currentProject = New ProjectSettings() With {
            .ProjectName = projName,
            .ProjectType = projType,
            .LastModified = DateTime.Now
        }
        _projectFilePath = ""
        _isDirty = True
    End Sub

    ''' <summary>Save project to a .fbproj JSON file.</summary>
    Public Function SaveProject(filePath As String) As Boolean
        Try
            CurrentProject.LastModified = DateTime.Now
            Dim json = JsonConvert.SerializeObject(CurrentProject, Formatting.Indented)
            File.WriteAllText(filePath, json)
            _projectFilePath = filePath
            _isDirty = False
            Return True
        Catch ex As Exception
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to save project: {filePath}", ex)
            Return False
        End Try
    End Function

    ''' <summary>Load project from a .fbproj JSON file.</summary>
    Public Function LoadProject(filePath As String) As Boolean
        Try
            If Not File.Exists(filePath) Then Return False
            Dim json = File.ReadAllText(filePath)
            _currentProject = JsonConvert.DeserializeObject(Of ProjectSettings)(json)
            _projectFilePath = filePath
            _isDirty = False
            Return True
        Catch ex As Exception
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to load project: {filePath}", ex)
            Return False
        End Try
    End Function

    ''' <summary>Get extra FBC flags needed for the current project type.</summary>
    Public Function GetProjectCompilerFlags() As String
        Select Case CurrentProject.ProjectType
            Case ProjectType.GUIApp
                Return "-s gui"
            Case ProjectType.Window9FormsApp
                Dim flags = "-s gui"
                If Not String.IsNullOrEmpty(CurrentProject.Window9IncludePath) Then
                    flags &= " -i """ & CurrentProject.Window9IncludePath & """"
                End If
                If Not String.IsNullOrEmpty(CurrentProject.Window9LibPath) Then
                    flags &= " -p """ & CurrentProject.Window9LibPath & """"
                End If
                Return flags
            Case Else
                Return ""
        End Select
    End Function

    ''' <summary>Try to auto-detect Window9 installation by checking common paths.</summary>
    Public Function AutoDetectWindow9Path() As String
        ' Check common FreeBASIC include locations
        Dim fbcPath = AppGlobals.Build.FBCPath
        If Not String.IsNullOrEmpty(fbcPath) Then
            Dim fbcDir = Path.GetDirectoryName(fbcPath)
            If Not String.IsNullOrEmpty(fbcDir) Then
                ' Check fbc/inc directory
                Dim incDir = Path.Combine(fbcDir, "inc")
                If File.Exists(Path.Combine(incDir, "window9.bi")) Then
                    Return incDir
                End If
                ' Check parent/inc
                Dim parentInc = Path.Combine(Path.GetDirectoryName(fbcDir), "inc")
                If File.Exists(Path.Combine(parentInc, "window9.bi")) Then
                    Return parentInc
                End If
            End If
        End If
        Return ""
    End Function

    ''' <summary>Save form design to a .w9form JSON file alongside the project.</summary>
    Public Function SaveFormDesign(design As W9FormDesign, filePath As String) As Boolean
        Try
            Dim json = JsonConvert.SerializeObject(design, Formatting.Indented)
            File.WriteAllText(filePath, json)
            Return True
        Catch ex As Exception
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to save form design: {filePath}", ex)
            Return False
        End Try
    End Function

    ''' <summary>Load form design from a .w9form JSON file.</summary>
    Public Function LoadFormDesign(filePath As String) As W9FormDesign
        Try
            If Not File.Exists(filePath) Then Return Nothing
            Dim json = File.ReadAllText(filePath)
            Return JsonConvert.DeserializeObject(Of W9FormDesign)(json)
        Catch ex As Exception
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to load form design: {filePath}", ex)
            Return Nothing
        End Try
    End Function

    ''' <summary>Save a multi-form project to a .w9form JSON file.</summary>
    Public Function SaveFormProject(proj As W9FormProject, filePath As String) As Boolean
        Try
            Dim json = JsonConvert.SerializeObject(proj, Formatting.Indented)
            File.WriteAllText(filePath, json)
            Return True
        Catch ex As Exception
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to save form project: {filePath}", ex)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Load a multi-form project from a .w9form JSON file.
    ''' Backward compatible: if the file contains a single W9FormDesign (old format),
    ''' it wraps it into a W9FormProject automatically.
    ''' </summary>
    Public Function LoadFormProject(filePath As String) As W9FormProject
        Try
            If Not File.Exists(filePath) Then Return Nothing
            Dim json = File.ReadAllText(filePath)

            ' Try loading as project first (new format)
            Try
                ' Use Replace mode so the constructor's default Forms list gets replaced
                ' instead of appended to by the deserializer
                Dim settings As New JsonSerializerSettings() With {
                    .ObjectCreationHandling = ObjectCreationHandling.Replace
                }
                Dim proj = JsonConvert.DeserializeObject(Of W9FormProject)(json, settings)
                If proj IsNot Nothing AndAlso proj.Forms IsNot Nothing AndAlso proj.Forms.Count > 0 Then
                    Return proj
                End If
            Catch ex As Exception
                ' Not a project format — try single form
                DiagnosticsLogger.LogInfo("ProjectManager", $"Not a project format, trying single form: {ex.Message}")
            End Try

            ' Fall back to single form (old format) → wrap in project
            Dim singleForm = JsonConvert.DeserializeObject(Of W9FormDesign)(json)
            If singleForm IsNot Nothing Then
                Dim proj As New W9FormProject()
                proj.Forms.Clear()
                singleForm.FormType = W9FormType.MainForm
                If String.IsNullOrEmpty(singleForm.VarName) Then singleForm.VarName = "hMainForm"
                proj.Forms.Add(singleForm)
                Return proj
            End If

            Return Nothing
        Catch ex As Exception
            DiagnosticsLogger.LogError("ProjectManager", $"Failed to load form project: {filePath}", ex)
            Return Nothing
        End Try
    End Function

End Module
