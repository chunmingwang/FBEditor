Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Dialog for selecting a project template when creating a new file.
''' </summary>
Public Class NewFromTemplateForm
    Inherits Form

    Private lstTemplates As ListBox
    Private lblDescription As Label
    Private btnCreate As Button
    Private btnCancel As Button

    ''' <summary>The selected template index, or -1 if cancelled.</summary>
    Public SelectedTemplateIndex As Integer = -1

    Public Sub New()
        Me.Text = "New from Template"
        Me.FormBorderStyle = FormBorderStyle.FixedDialog
        Me.MaximizeBox = False
        Me.MinimizeBox = False
        Me.StartPosition = FormStartPosition.CenterParent
        Me.Size = New Size(450, 340)
        Me.ShowInTaskbar = False

        Dim lblTitle As New Label() With {
            .Text = "Select a template:",
            .Location = New Point(12, 12),
            .AutoSize = True
        }

        lstTemplates = New ListBox() With {
            .Location = New Point(12, 32),
            .Size = New Size(410, 150)
        }

        ' Populate with template names
        For Each t In ProjectTemplates.Templates
            lstTemplates.Items.Add(t.Name)
        Next
        If lstTemplates.Items.Count > 0 Then lstTemplates.SelectedIndex = 0

        AddHandler lstTemplates.SelectedIndexChanged, AddressOf OnSelectionChanged
        AddHandler lstTemplates.DoubleClick, AddressOf OnCreate

        lblDescription = New Label() With {
            .Location = New Point(12, 190),
            .Size = New Size(410, 50),
            .Text = ""
        }
        UpdateDescription()

        btnCreate = New Button() With {
            .Text = "Create",
            .Location = New Point(260, 260),
            .Width = 80,
            .Height = 28,
            .DialogResult = DialogResult.OK
        }
        AddHandler btnCreate.Click, AddressOf OnCreate

        btnCancel = New Button() With {
            .Text = "Cancel",
            .Location = New Point(345, 260),
            .Width = 80,
            .Height = 28,
            .DialogResult = DialogResult.Cancel
        }

        Me.Controls.AddRange(New Control() {lblTitle, lstTemplates, lblDescription, btnCreate, btnCancel})
        Me.AcceptButton = btnCreate
        Me.CancelButton = btnCancel
    End Sub

    Private Sub OnSelectionChanged(sender As Object, e As EventArgs)
        UpdateDescription()
    End Sub

    Private Sub UpdateDescription()
        If lstTemplates.SelectedIndex >= 0 AndAlso lstTemplates.SelectedIndex < ProjectTemplates.Templates.Length Then
            lblDescription.Text = ProjectTemplates.Templates(lstTemplates.SelectedIndex).Description
        Else
            lblDescription.Text = ""
        End If
    End Sub

    Private Sub OnCreate(sender As Object, e As EventArgs)
        If lstTemplates.SelectedIndex >= 0 Then
            SelectedTemplateIndex = lstTemplates.SelectedIndex
            Me.DialogResult = DialogResult.OK
            Me.Close()
        End If
    End Sub

    Public Sub ApplyTheme(isDark As Boolean)
        If isDark Then
            Me.BackColor = Color.FromArgb(45, 45, 48)
            Me.ForeColor = Color.FromArgb(220, 220, 220)
            lstTemplates.BackColor = Color.FromArgb(51, 51, 55)
            lstTemplates.ForeColor = Color.FromArgb(220, 220, 220)
            btnCreate.BackColor = Color.FromArgb(62, 62, 66)
            btnCreate.ForeColor = Color.FromArgb(220, 220, 220)
            btnCancel.BackColor = Color.FromArgb(62, 62, 66)
            btnCancel.ForeColor = Color.FromArgb(220, 220, 220)
        Else
            Me.BackColor = SystemColors.Control
            Me.ForeColor = SystemColors.ControlText
            lstTemplates.BackColor = SystemColors.Window
            lstTemplates.ForeColor = SystemColors.WindowText
            btnCreate.BackColor = SystemColors.Control
            btnCreate.ForeColor = SystemColors.ControlText
            btnCancel.BackColor = SystemColors.Control
            btnCancel.ForeColor = SystemColors.ControlText
        End If
    End Sub
End Class
