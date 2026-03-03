Imports System.IO
Imports System.Drawing
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Forms

''' <summary>
''' Find in Files dialog — searches for text across multiple files in a directory.
''' Results are displayed in the main form's Find Results output tab.
''' </summary>
Public Class FindInFilesForm
    Inherits Form

    Private txtSearch As TextBox
    Private txtFilter As TextBox
    Private txtDirectory As TextBox
    Private chkMatchCase As CheckBox
    Private chkWholeWord As CheckBox
    Private chkRegex As CheckBox
    Private btnSearch As Button
    Private btnBrowse As Button
    Private btnClose As Button
    Private lblStatus As Label

    ''' <summary>Fired for each search result found. (filePath, lineNumber, lineText)</summary>
    Public Event ResultFound(filePath As String, lineNumber As Integer, lineText As String)
    ''' <summary>Fired when search starts (clears previous results).</summary>
    Public Event SearchStarted(searchText As String)
    ''' <summary>Fired when search completes.</summary>
    Public Event SearchCompleted(totalMatches As Integer, totalFiles As Integer)

    Public Sub New()
        Me.Text = "Find in Files"
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Size = New Size(520, 300)
        Me.ShowInTaskbar = False

        ' Search text
        Dim lblSearch As New Label() With {.Text = "Search text:", .Location = New Point(12, 16), .AutoSize = True}
        txtSearch = New TextBox() With {.Location = New Point(100, 13), .Width = 300}
        Me.Controls.AddRange({lblSearch, txtSearch})

        ' File filter
        Dim lblFilter As New Label() With {.Text = "File filter:", .Location = New Point(12, 46), .AutoSize = True}
        txtFilter = New TextBox() With {.Location = New Point(100, 43), .Width = 300, .Text = "*.bas;*.bi"}
        Me.Controls.AddRange({lblFilter, txtFilter})

        ' Directory
        Dim lblDir As New Label() With {.Text = "Directory:", .Location = New Point(12, 76), .AutoSize = True}
        txtDirectory = New TextBox() With {.Location = New Point(100, 73), .Width = 262}
        btnBrowse = New Button() With {.Text = "...", .Location = New Point(368, 72), .Width = 32, .Height = 23}
        AddHandler btnBrowse.Click, AddressOf OnBrowse
        Me.Controls.AddRange({lblDir, txtDirectory, btnBrowse})

        ' Options
        chkMatchCase = New CheckBox() With {.Text = "Match case", .Location = New Point(100, 106), .AutoSize = True}
        chkWholeWord = New CheckBox() With {.Text = "Whole word", .Location = New Point(210, 106), .AutoSize = True}
        chkRegex = New CheckBox() With {.Text = "Regex", .Location = New Point(320, 106), .AutoSize = True}
        Me.Controls.AddRange({chkMatchCase, chkWholeWord, chkRegex})

        ' Buttons
        btnSearch = New Button() With {.Text = "Find All", .Location = New Point(100, 140), .Width = 80, .Height = 28}
        AddHandler btnSearch.Click, AddressOf OnSearch
        btnClose = New Button() With {.Text = "Close", .Location = New Point(190, 140), .Width = 80, .Height = 28}
        AddHandler btnClose.Click, Sub(s, e) Me.Hide()
        Me.Controls.AddRange({btnSearch, btnClose})

        ' Status
        lblStatus = New Label() With {.Text = "", .Location = New Point(100, 178), .AutoSize = True}
        Me.Controls.Add(lblStatus)

        Me.AcceptButton = btnSearch
        Me.CancelButton = btnClose
    End Sub

    ''' <summary>Set the initial search directory.</summary>
    Public Sub SetDirectory(dir As String)
        txtDirectory.Text = dir
    End Sub

    ''' <summary>Set the initial search text from current selection.</summary>
    Public Sub SetSearchText(text As String)
        If Not String.IsNullOrEmpty(text) Then txtSearch.Text = text
        txtSearch.SelectAll()
        txtSearch.Focus()
    End Sub

    Private Sub OnBrowse(sender As Object, e As EventArgs)
        Using fbd As New FolderBrowserDialog()
            fbd.SelectedPath = txtDirectory.Text
            If fbd.ShowDialog() = DialogResult.OK Then
                txtDirectory.Text = fbd.SelectedPath
            End If
        End Using
    End Sub

    Private Sub OnSearch(sender As Object, e As EventArgs)
        Dim searchText = txtSearch.Text
        If String.IsNullOrEmpty(searchText) Then
            MessageBox.Show("Please enter search text.", "Find in Files", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        Dim dir = txtDirectory.Text
        If String.IsNullOrEmpty(dir) OrElse Not Directory.Exists(dir) Then
            MessageBox.Show("Please select a valid directory.", "Find in Files", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim filters = txtFilter.Text.Split({";"c}, StringSplitOptions.RemoveEmptyEntries)
        Dim matchCase = chkMatchCase.Checked
        Dim wholeWord = chkWholeWord.Checked
        Dim useRegex = chkRegex.Checked

        btnSearch.Enabled = False
        lblStatus.Text = "Searching..."
        RaiseEvent SearchStarted(searchText)

        Dim ctx = SynchronizationContext.Current
        ThreadPool.QueueUserWorkItem(
            Sub()
                Dim totalMatches = 0
                Dim totalFiles = 0
                Try
                    ' Collect all files matching filters
                    Dim files As New List(Of String)()
                    For Each flt In filters
                        Try
                            files.AddRange(Directory.EnumerateFiles(dir, flt.Trim(), SearchOption.AllDirectories))
                        Catch ex As Exception
                            ' Skip inaccessible directories
                        End Try
                    Next
                    files = files.Distinct().ToList()

                    ' Build regex pattern
                    Dim pattern As String
                    If useRegex Then
                        pattern = searchText
                    Else
                        pattern = Regex.Escape(searchText)
                    End If
                    If wholeWord Then pattern = "\b" & pattern & "\b"

                    Dim options = If(matchCase, RegexOptions.None, RegexOptions.IgnoreCase)
                    Dim rx As New Regex(pattern, options)

                    For Each filePath In files
                        Try
                            Dim lines = File.ReadAllLines(filePath)
                            Dim fileHasMatch = False
                            For lineIdx = 0 To lines.Length - 1
                                If rx.IsMatch(lines(lineIdx)) Then
                                    totalMatches += 1
                                    If Not fileHasMatch Then totalFiles += 1 : fileHasMatch = True
                                    Dim ln = lineIdx + 1
                                    Dim lineText = lines(lineIdx).TrimStart()
                                    ctx.Post(Sub(s)
                                                 RaiseEvent ResultFound(filePath, ln, lineText)
                                             End Sub, Nothing)
                                End If
                            Next
                        Catch
                            ' Skip unreadable files
                        End Try
                    Next
                Catch ex As Exception
                    DiagnosticsLogger.LogError("FindInFiles", "Search failed", ex)
                End Try

                Dim fm = totalMatches
                Dim ff = totalFiles
                ctx.Post(Sub(s)
                             btnSearch.Enabled = True
                             lblStatus.Text = $"Found {fm} match(es) in {ff} file(s)."
                             RaiseEvent SearchCompleted(fm, ff)
                         End Sub, Nothing)
            End Sub)
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        ' Hide instead of close — reuse the form instance
        If e.CloseReason = CloseReason.UserClosing Then
            e.Cancel = True
            Me.Hide()
        End If
    End Sub

    Public Sub ApplyTheme(isDark As Boolean)
        If isDark Then
            Me.BackColor = Color.FromArgb(45, 45, 48)
            Me.ForeColor = Color.FromArgb(220, 220, 220)
            For Each ctrl As Control In Me.Controls
                If TypeOf ctrl Is TextBox Then
                    ctrl.BackColor = Color.FromArgb(51, 51, 55) : ctrl.ForeColor = Color.FromArgb(220, 220, 220)
                ElseIf TypeOf ctrl Is Button Then
                    ctrl.BackColor = Color.FromArgb(62, 62, 66) : ctrl.ForeColor = Color.FromArgb(220, 220, 220)
                End If
            Next
        Else
            Me.BackColor = SystemColors.Control
            Me.ForeColor = SystemColors.ControlText
            For Each ctrl As Control In Me.Controls
                If TypeOf ctrl Is TextBox Then
                    ctrl.BackColor = SystemColors.Window : ctrl.ForeColor = SystemColors.WindowText
                ElseIf TypeOf ctrl Is Button Then
                    ctrl.BackColor = SystemColors.Control : ctrl.ForeColor = SystemColors.ControlText
                End If
            Next
        End If
    End Sub
End Class
