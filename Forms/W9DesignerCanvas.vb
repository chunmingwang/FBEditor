Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' Visual form designer canvas — the main design surface where users place,
''' move, resize, and arrange Window9 gadgets. Mimics Visual Studio's form designer.
''' Features: grid snapping, drag to move, resize handles, multi-select,
''' delete, copy/paste, and visual representation of each gadget type.
''' </summary>
Public Class W9DesignerCanvas
    Inherits Panel

    ' ---- Data ----
    Private _formDesign As W9FormDesign
    Private _project As W9FormProject
    Private _selectedGadget As W9GadgetInstance = Nothing
    Private _multiSelection As New List(Of W9GadgetInstance)()

    ' ---- Drag state ----
    Private _isDragging As Boolean = False
    Private _isResizing As Boolean = False
    Private _isDrawing As Boolean = False       ' Drawing a new gadget from toolbox
    Private _dragStart As Point
    Private _dragOffset As Point
    Private _resizeHandle As ResizeHandleType = ResizeHandleType.None
    Private _drawStartPoint As Point
    Private _drawCurrentPoint As Point

    ' ---- New gadget being drawn ----
    Private _pendingGadgetType As W9GadgetType? = Nothing

    ' ---- Visual settings ----
    Private _gridSize As Integer = 8
    Private _snapToGrid As Boolean = True
    Private _showGrid As Boolean = True
    Private _zoom As Single = 1.0F

    ' ---- Colors ----
    Private _formBackColor As Color = Color.FromArgb(240, 240, 240)
    Private _gridColor As Color = Color.FromArgb(220, 220, 220)
    Private _selectionColor As Color = Color.FromArgb(0, 120, 215)
    Private _handleColor As Color = Color.White
    Private _handleBorderColor As Color = Color.FromArgb(0, 120, 215)

    ' ---- Resize handles ----
    Private Const HANDLE_SIZE As Integer = 9

    ' ---- Title bar offset (gadgets are below the simulated title bar) ----
    Private Const TITLE_BAR_H As Integer = 30

    ' ---- Non-client area overhead (title bar + borders on Windows 10/11) ----
    ' These approximate the space consumed by the OS window frame
    Private Const NC_TOP As Integer = 31      ' Title bar height
    Private Const NC_BOTTOM As Integer = 8    ' Bottom border
    Private Const NC_SIDES As Integer = 8     ' Left + right border (each side)

    Public Enum ResizeHandleType
        None = 0
        TopLeft
        TopCenter
        TopRight
        MiddleLeft
        MiddleRight
        BottomLeft
        BottomCenter
        BottomRight
    End Enum

    ' ---- Events ----
    Public Event GadgetSelected(gadget As W9GadgetInstance)
    Public Event GadgetMoved(gadget As W9GadgetInstance)
    Public Event GadgetResized(gadget As W9GadgetInstance)
    Public Event GadgetAdded(gadget As W9GadgetInstance)
    Public Event GadgetDeleted(gadget As W9GadgetInstance)
    Public Event FormSurfaceClicked()
    Public Event DesignChanged()

    ' ---- Undo stack ----
    Private _undoStack As New Stack(Of List(Of W9GadgetInstance))()
    Private _redoStack As New Stack(Of List(Of W9GadgetInstance))()

    ' ---- Clipboard (multi-gadget) ----
    Private _clipboard As List(Of W9GadgetInstance) = Nothing

    ' ---- Lasso selection ----
    Private _isLassoSelecting As Boolean = False
    Private _lassoStart As Point
    Private _lassoCurrent As Point

    ' ---- Snap lines ----
    Private _snapLines As New List(Of SnapLineInfo)()
    Private _snapLinesEnabled As Boolean = True
    Private Const SNAP_THRESHOLD As Integer = 6

    Public Class SnapLineInfo
        Public IsHorizontal As Boolean  ' True = horizontal line, False = vertical
        Public Position As Integer      ' Y for horizontal, X for vertical
        Public Start As Integer         ' Start of line extent
        Public [End] As Integer         ' End of line extent
    End Class

    ' =========================================================================
    ' Constructor
    ' =========================================================================
    Public Sub New()
        Me.DoubleBuffered = True
        Me.SetStyle(ControlStyles.AllPaintingInWmPaint Or
                    ControlStyles.UserPaint Or
                    ControlStyles.OptimizedDoubleBuffer Or
                    ControlStyles.Selectable, True)
        Me.BackColor = _formBackColor
        Me.AllowDrop = True
        Me.AutoScroll = False  ' Wrapper panel handles scrolling
        Me.TabStop = True

        _formDesign = New W9FormDesign()
        BuildContextMenu()
    End Sub

    ' =========================================================================
    ' Properties
    ' =========================================================================
    Public Property FormDesign As W9FormDesign
        Get
            Return _formDesign
        End Get
        Set(value As W9FormDesign)
            _formDesign = If(value, New W9FormDesign())
            _selectedGadget = Nothing
            _multiSelection.Clear()
            Invalidate()
        End Set
    End Property

    ''' <summary>The parent project — used to ensure unique enum names across all forms.</summary>
    Public Property Project As W9FormProject
        Get
            Return _project
        End Get
        Set(value As W9FormProject)
            _project = value
        End Set
    End Property

    Public Property SelectedGadget As W9GadgetInstance
        Get
            Return _selectedGadget
        End Get
        Set(value As W9GadgetInstance)
            _formDesign.ClearSelection()
            _selectedGadget = value
            If value IsNot Nothing Then value.IsSelected = True
            Invalidate()
            RaiseEvent GadgetSelected(value)
        End Set
    End Property

    Public Property GridSize As Integer
        Get
            Return _gridSize
        End Get
        Set(value As Integer)
            _gridSize = Math.Max(4, Math.Min(32, value))
            Invalidate()
        End Set
    End Property

    Public Property SnapToGrid As Boolean
        Get
            Return _snapToGrid
        End Get
        Set(value As Boolean)
            _snapToGrid = value
        End Set
    End Property

    Public Property ShowGrid As Boolean
        Get
            Return _showGrid
        End Get
        Set(value As Boolean)
            _showGrid = value
            Invalidate()
        End Set
    End Property

    Public Property SnapLinesEnabled As Boolean
        Get
            Return _snapLinesEnabled
        End Get
        Set(value As Boolean)
            _snapLinesEnabled = value
        End Set
    End Property

    ''' <summary>Set the gadget type to draw next (from toolbox click).</summary>
    Public Sub SetPendingGadgetType(gt As W9GadgetType)
        _pendingGadgetType = gt
        Me.Cursor = Cursors.Cross
    End Sub

    Public Sub ClearPendingGadgetType()
        _pendingGadgetType = Nothing
        Me.Cursor = Cursors.Default
    End Sub

    ' =========================================================================
    ' Painting
    ' =========================================================================
    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.TextRenderingHint = Drawing.Text.TextRenderingHint.ClearTypeGridFit

        ' Canvas represents the CLIENT AREA of the Window9 window.
        ' Y=0 here = Y=0 in generated code = top of client area (below real title bar).
        Dim formRect As New Rectangle(0, 0,
            CInt(_formDesign.FormWidth * _zoom),
            CInt(_formDesign.FormHeight * _zoom))
        Using fb As New SolidBrush(_formBackColor)
            g.FillRectangle(fb, formRect)
        End Using

        ' Grid
        If _showGrid Then DrawGrid(g, formRect)

        ' Form border
        Using fp As New Pen(Color.FromArgb(100, 100, 100), 1)
            fp.DashStyle = DashStyle.Dash
            g.DrawRectangle(fp, formRect)
        End Using

        ' Draw all gadgets at their raw coordinates
        ' Y=10 here = Y=10 in Window9 = 10px below title bar in running app
        For Each gad In _formDesign.Gadgets
            DrawGadget(g, gad)
        Next

        ' Selection handles
        If _selectedGadget IsNot Nothing Then
            DrawSelectionHandles(g, _selectedGadget)
        End If

        ' Multi-selection
        For Each sel In _multiSelection
            If sel IsNot _selectedGadget Then
                DrawSelectionHandles(g, sel)
            End If
        Next

        ' Drawing rectangle (when placing new gadget)
        If _isDrawing Then
            Dim drawRect = GetNormalizedRect(_drawStartPoint, _drawCurrentPoint)
            Using dp As New Pen(_selectionColor, 1)
                dp.DashStyle = DashStyle.Dash
                g.DrawRectangle(dp, drawRect)
            End Using
        End If

        ' Lasso selection rectangle
        If _isLassoSelecting Then
            Dim lassoRect = GetNormalizedRect(_lassoStart, _lassoCurrent)
            Using lassoBrush As New SolidBrush(Color.FromArgb(40, 0, 120, 215))
                g.FillRectangle(lassoBrush, lassoRect)
            End Using
            Using lassoPen As New Pen(Color.FromArgb(160, 0, 120, 215), 1)
                lassoPen.DashStyle = DashStyle.Dash
                g.DrawRectangle(lassoPen, lassoRect)
            End Using
        End If

        ' Snap lines (alignment guidelines)
        If _snapLines.Count > 0 Then
            Using snapPen As New Pen(Color.FromArgb(255, 255, 0, 151), 1)
                snapPen.DashStyle = DashStyle.Dash
                For Each sl In _snapLines
                    If sl.IsHorizontal Then
                        g.DrawLine(snapPen, CInt(sl.Start * _zoom), CInt(sl.Position * _zoom),
                                   CInt(sl.End * _zoom), CInt(sl.Position * _zoom))
                    Else
                        g.DrawLine(snapPen, CInt(sl.Position * _zoom), CInt(sl.Start * _zoom),
                                   CInt(sl.Position * _zoom), CInt(sl.End * _zoom))
                    End If
                Next
            End Using
        End If

        ' Lock indicator on locked gadgets
        For Each gad In _formDesign.Gadgets
            If gad.IsLocked Then
                Dim lx = CInt(gad.X * _zoom) + 2
                Dim ly = CInt(gad.Y * _zoom) + 2
                Using lockBrush As New SolidBrush(Color.FromArgb(180, 255, 100, 100))
                    g.FillEllipse(lockBrush, lx, ly, 12, 12)
                End Using
                Using lockFont As New Font("Segoe UI", 7, FontStyle.Bold)
                    g.DrawString("L", lockFont, Brushes.White, lx + 1, ly)
                End Using
            End If
        Next

        ' Tab order overlay
        If _showTabOrder Then
            For i = 0 To _formDesign.Gadgets.Count - 1
                Dim gad = _formDesign.Gadgets(i)
                Dim cx = CInt((gad.X + gad.W / 2) * _zoom)
                Dim cy = CInt((gad.Y + gad.H / 2) * _zoom)
                Dim numStr = (i + 1).ToString()
                ' Blue circle with number
                Using tabBrush As New SolidBrush(Color.FromArgb(210, 0, 100, 220))
                    g.FillEllipse(tabBrush, cx - 12, cy - 12, 24, 24)
                End Using
                Using tabPen As New Pen(Color.White, 1.5F)
                    g.DrawEllipse(tabPen, cx - 12, cy - 12, 24, 24)
                End Using
                Using tabFont As New Font("Segoe UI", 9, FontStyle.Bold)
                    Dim sz = g.MeasureString(numStr, tabFont)
                    g.DrawString(numStr, tabFont, Brushes.White, cx - sz.Width / 2, cy - sz.Height / 2)
                End Using
            Next
        End If
    End Sub

    Private Sub DrawGrid(g As Graphics, area As Rectangle)
        Dim gs = CInt(_gridSize * _zoom)
        If gs < 4 Then Return
        Using gridBrush As New SolidBrush(_gridColor)
            For x = area.X To area.X + area.Width Step gs
                For y = area.Y To area.Y + area.Height Step gs
                    g.FillRectangle(gridBrush, x, y, 1, 1)
                Next
            Next
        End Using
    End Sub

    ''' <summary>Draw a gadget as it would appear on the form (simplified visual).</summary>
    Private Sub DrawGadget(g As Graphics, gad As W9GadgetInstance)
        Dim rect As New Rectangle(
            CInt(gad.X * _zoom), CInt(gad.Y * _zoom),
            CInt(gad.W * _zoom), CInt(gad.H * _zoom))
        Dim tdef = W9GadgetRegistry.GetTypeDef(gad.GadgetType)
        Dim displayText = If(Not String.IsNullOrEmpty(gad.Text), gad.Text, gad.EnumName)
        Dim font = New Font("Segoe UI", Math.Max(7, 8 * _zoom), FontStyle.Regular)

        Select Case gad.GadgetType
            Case W9GadgetType.Button
                DrawButtonGadget(g, rect, displayText, font)

            Case W9GadgetType.TextLabel
                DrawLabelGadget(g, rect, displayText, font)

            Case W9GadgetType.Editor
                DrawEditorGadget(g, rect, displayText, font, gad.IsReadOnly)

            Case W9GadgetType.StringInput
                DrawStringGadget(g, rect, displayText, font)

            Case W9GadgetType.CheckBox
                DrawCheckBoxGadget(g, rect, displayText, font)

            Case W9GadgetType.OptionButton
                DrawOptionGadget(g, rect, displayText, font)

            Case W9GadgetType.ComboBox
                DrawComboBoxGadget(g, rect, font)

            Case W9GadgetType.ListBox
                DrawListBoxGadget(g, rect, font)

            Case W9GadgetType.GroupBox
                DrawGroupGadget(g, rect, displayText, font)

            Case W9GadgetType.ProgressBar
                DrawProgressBarGadget(g, rect)

            Case W9GadgetType.ScrollBar
                DrawScrollBarGadget(g, rect, gad.Orientation)

            Case W9GadgetType.TrackBar
                DrawTrackBarGadget(g, rect)

            Case W9GadgetType.StatusBar
                DrawStatusBarGadget(g, rect, gad)

            Case W9GadgetType.PanelTab
                DrawPanelGadget(g, rect, font)

            Case W9GadgetType.Container
                DrawContainerGadget(g, rect, font, gad.EnumName)

            Case Else
                DrawGenericGadget(g, rect, displayText, font, tdef)
        End Select

        font.Dispose()
    End Sub

    ' ---- Individual gadget painters ----

    Private Sub DrawButtonGadget(g As Graphics, r As Rectangle, text As String, f As Font)
        Using br As New SolidBrush(Color.FromArgb(225, 225, 225))
            g.FillRectangle(br, r)
        End Using
        ControlPaint.DrawBorder3D(g, r, Border3DStyle.Raised)
        Dim sf As New StringFormat() With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
        g.DrawString(text, f, Brushes.Black, New RectangleF(r.X, r.Y, r.Width, r.Height), sf)
    End Sub

    Private Sub DrawLabelGadget(g As Graphics, r As Rectangle, text As String, f As Font)
        g.DrawString(text, f, Brushes.Black, r.X + 2, r.Y + 2)
        Using bp As New Pen(Color.FromArgb(180, 180, 180), 1)
            bp.DashStyle = DashStyle.Dot
            g.DrawRectangle(bp, r)
        End Using
    End Sub

    Private Sub DrawEditorGadget(g As Graphics, r As Rectangle, text As String, f As Font, isReadOnlyEditor As Boolean)
        Dim bgColor = If(isReadOnlyEditor, Color.FromArgb(245, 245, 245), Color.White)
        Using br As New SolidBrush(bgColor)
            g.FillRectangle(br, r)
        End Using
        ControlPaint.DrawBorder3D(g, r, Border3DStyle.Sunken)
        If Not String.IsNullOrEmpty(text) Then
            Dim textRect = Rectangle.Inflate(r, -3, -3)
            g.DrawString(text, f, Brushes.Gray, New RectangleF(textRect.X, textRect.Y, textRect.Width, textRect.Height))
        End If
        ' Editor label
        Using ef = New Font("Segoe UI", 6.5F * _zoom, FontStyle.Italic)
            Dim label = If(isReadOnlyEditor, "Editor (ReadOnly)", "Editor")
            g.DrawString(label, ef, Brushes.Gray, r.X + 3, r.Bottom - 14 * _zoom)
        End Using
    End Sub

    Private Sub DrawStringGadget(g As Graphics, r As Rectangle, text As String, f As Font)
        g.FillRectangle(Brushes.White, r)
        ControlPaint.DrawBorder3D(g, r, Border3DStyle.Sunken)
        If Not String.IsNullOrEmpty(text) Then
            g.DrawString(text, f, Brushes.Gray, r.X + 3, r.Y + 2)
        End If
    End Sub

    Private Sub DrawCheckBoxGadget(g As Graphics, r As Rectangle, text As String, f As Font)
        Using bp As New Pen(Color.FromArgb(180, 180, 180), 1)
            bp.DashStyle = DashStyle.Dot
            g.DrawRectangle(bp, r)
        End Using
        Dim boxRect As New Rectangle(r.X + 2, r.Y + (r.Height - 14) \ 2, 14, 14)
        g.FillRectangle(Brushes.White, boxRect)
        g.DrawRectangle(Pens.Gray, boxRect)
        g.DrawString(text, f, Brushes.Black, r.X + 20, r.Y + 2)
    End Sub

    Private Sub DrawOptionGadget(g As Graphics, r As Rectangle, text As String, f As Font)
        Using bp As New Pen(Color.FromArgb(180, 180, 180), 1)
            bp.DashStyle = DashStyle.Dot
            g.DrawRectangle(bp, r)
        End Using
        Dim circleRect As New Rectangle(r.X + 2, r.Y + (r.Height - 14) \ 2, 14, 14)
        g.FillEllipse(Brushes.White, circleRect)
        g.DrawEllipse(Pens.Gray, circleRect)
        g.DrawString(text, f, Brushes.Black, r.X + 20, r.Y + 2)
    End Sub

    Private Sub DrawComboBoxGadget(g As Graphics, r As Rectangle, f As Font)
        g.FillRectangle(Brushes.White, r)
        ControlPaint.DrawBorder3D(g, r, Border3DStyle.Sunken)
        ' Draw dropdown arrow
        Dim arrowRect As New Rectangle(r.Right - 20, r.Y, 20, r.Height)
        Using br As New SolidBrush(Color.FromArgb(225, 225, 225))
            g.FillRectangle(br, arrowRect)
        End Using
        g.DrawLine(Pens.Gray, arrowRect.X, r.Y, arrowRect.X, r.Bottom)
        ' Arrow triangle
        Dim cx = arrowRect.X + 10
        Dim cy = r.Y + r.Height \ 2
        g.FillPolygon(Brushes.Black, New Point() {New Point(cx - 4, cy - 2), New Point(cx + 4, cy - 2), New Point(cx, cy + 3)})
    End Sub

    Private Sub DrawListBoxGadget(g As Graphics, r As Rectangle, f As Font)
        g.FillRectangle(Brushes.White, r)
        ControlPaint.DrawBorder3D(g, r, Border3DStyle.Sunken)
        Using ef = New Font("Segoe UI", 6.5F * _zoom, FontStyle.Italic)
            g.DrawString("ListBox", ef, Brushes.Gray, r.X + 3, r.Y + 3)
        End Using
    End Sub

    Private Sub DrawGroupGadget(g As Graphics, r As Rectangle, text As String, f As Font)
        Using bp As New Pen(Color.FromArgb(160, 160, 160), 1)
            Dim textSize = g.MeasureString(text, f)
            ' Draw the group border with text gap
            Dim topY = r.Y + CInt(textSize.Height / 2)
            g.DrawLine(bp, r.X, topY, r.X + 8, topY)
            g.DrawString(text, f, Brushes.Black, r.X + 10, r.Y)
            g.DrawLine(bp, r.X + 14 + CInt(textSize.Width), topY, r.Right, topY)
            g.DrawLine(bp, r.X, topY, r.X, r.Bottom)
            g.DrawLine(bp, r.Right, topY, r.Right, r.Bottom)
            g.DrawLine(bp, r.X, r.Bottom, r.Right, r.Bottom)
        End Using
    End Sub

    Private Sub DrawProgressBarGadget(g As Graphics, r As Rectangle)
        g.FillRectangle(Brushes.White, r)
        ControlPaint.DrawBorder3D(g, r, Border3DStyle.Sunken)
        ' Draw partial fill
        Dim fillRect As New Rectangle(r.X + 2, r.Y + 2, CInt((r.Width - 4) * 0.6), r.Height - 4)
        Using br As New SolidBrush(Color.FromArgb(6, 176, 37))
            g.FillRectangle(br, fillRect)
        End Using
    End Sub

    Private Sub DrawScrollBarGadget(g As Graphics, r As Rectangle, orientation As Integer)
        Using br As New SolidBrush(Color.FromArgb(230, 230, 230))
            g.FillRectangle(br, r)
        End Using
        g.DrawRectangle(Pens.Gray, r)
        ' Draw thumb
        If orientation = 0 Then ' Horizontal
            Dim thumbRect As New Rectangle(r.X + r.Width \ 3, r.Y + 2, r.Width \ 3, r.Height - 4)
            Using tb As New SolidBrush(Color.FromArgb(190, 190, 190))
                g.FillRectangle(tb, thumbRect)
            End Using
        Else ' Vertical
            Dim thumbRect As New Rectangle(r.X + 2, r.Y + r.Height \ 3, r.Width - 4, r.Height \ 3)
            Using tb As New SolidBrush(Color.FromArgb(190, 190, 190))
                g.FillRectangle(tb, thumbRect)
            End Using
        End If
    End Sub

    Private Sub DrawTrackBarGadget(g As Graphics, r As Rectangle)
        Using bp As New Pen(Color.FromArgb(180, 180, 180), 1)
            bp.DashStyle = DashStyle.Dot
            g.DrawRectangle(bp, r)
        End Using
        ' Track line
        Dim trackY = r.Y + r.Height \ 2
        g.DrawLine(Pens.Gray, r.X + 10, trackY, r.Right - 10, trackY)
        ' Thumb
        Dim thumbX = r.X + r.Width \ 2
        Dim thumbRect As New Rectangle(thumbX - 5, trackY - 8, 10, 16)
        Using br As New SolidBrush(Color.FromArgb(200, 200, 200))
            g.FillRectangle(br, thumbRect)
        End Using
        g.DrawRectangle(Pens.Gray, thumbRect)
    End Sub

    Private Sub DrawStatusBarGadget(g As Graphics, r As Rectangle, gad As W9GadgetInstance)
        ' StatusBar always at bottom of form
        Dim sbRect As New Rectangle(0, CInt((_formDesign.FormHeight - 24) * _zoom),
                                    CInt(_formDesign.FormWidth * _zoom), CInt(24 * _zoom))
        Using br As New SolidBrush(Color.FromArgb(225, 225, 225))
            g.FillRectangle(br, sbRect)
        End Using
        g.DrawLine(Pens.Gray, sbRect.X, sbRect.Y, sbRect.Right, sbRect.Y)
        Using sf = New Font("Segoe UI", 7 * _zoom)
            If gad.StatusBarFields.Count > 0 Then
                Dim xOff = 4
                For Each field In gad.StatusBarFields
                    g.DrawString(field.Text, sf, Brushes.Black, sbRect.X + xOff, sbRect.Y + 4)
                    If field.Width > 0 Then
                        xOff += CInt(field.Width * _zoom)
                        g.DrawLine(Pens.Gray, sbRect.X + xOff, sbRect.Y + 2, sbRect.X + xOff, sbRect.Bottom - 2)
                        xOff += 4
                    End If
                Next
            Else
                g.DrawString("StatusBar", sf, Brushes.Gray, sbRect.X + 4, sbRect.Y + 4)
            End If
        End Using
    End Sub

    Private Sub DrawPanelGadget(g As Graphics, r As Rectangle, f As Font)
        g.FillRectangle(Brushes.White, r)
        g.DrawRectangle(Pens.Gray, r)
        ' Tab header
        Dim tabRect As New Rectangle(r.X, r.Y, 60, 22)
        Using br As New SolidBrush(Color.FromArgb(240, 240, 240))
            g.FillRectangle(br, tabRect)
        End Using
        g.DrawRectangle(Pens.Gray, tabRect)
        Using ef = New Font("Segoe UI", 6.5F * _zoom)
            g.DrawString("Tab 1", ef, Brushes.Black, tabRect.X + 4, tabRect.Y + 3)
        End Using
    End Sub

    Private Sub DrawContainerGadget(g As Graphics, r As Rectangle, f As Font, name As String)
        Using bp As New Pen(Color.FromArgb(180, 180, 180), 1)
            bp.DashStyle = DashStyle.DashDot
            g.DrawRectangle(bp, r)
        End Using
        Using ef = New Font("Segoe UI", 6.5F * _zoom, FontStyle.Italic)
            g.DrawString(name, ef, Brushes.Gray, r.X + 3, r.Y + 3)
        End Using
    End Sub

    Private Sub DrawGenericGadget(g As Graphics, r As Rectangle, text As String, f As Font, tdef As W9GadgetTypeDef)
        Using br As New SolidBrush(Color.FromArgb(250, 250, 250))
            g.FillRectangle(br, r)
        End Using
        g.DrawRectangle(Pens.Gray, r)
        Dim label = If(tdef IsNot Nothing, tdef.DisplayName, "Gadget")
        Using ef = New Font("Segoe UI", 6.5F * _zoom, FontStyle.Italic)
            g.DrawString(label, ef, Brushes.Gray, r.X + 3, r.Y + 3)
        End Using
        If Not String.IsNullOrEmpty(text) Then
            g.DrawString(text, f, Brushes.Black, r.X + 3, r.Y + 16)
        End If
    End Sub

    ''' <summary>Draw selection handles around a gadget.</summary>
    Private Sub DrawSelectionHandles(g As Graphics, gad As W9GadgetInstance)
        Dim r As New Rectangle(CInt(gad.X * _zoom), CInt(gad.Y * _zoom),
                               CInt(gad.W * _zoom), CInt(gad.H * _zoom))
        ' Selection border
        Using sp As New Pen(_selectionColor, 1)
            sp.DashStyle = DashStyle.Dash
            g.DrawRectangle(sp, r)
        End Using

        ' 8 handles
        Dim handleRects = GetHandleRects(r)
        Using hBrush As New SolidBrush(_handleColor), hPen As New Pen(_handleBorderColor, 1)
            For Each hr In handleRects
                g.FillRectangle(hBrush, hr)
                g.DrawRectangle(hPen, hr)
            Next
        End Using
    End Sub

    Private Function GetHandleRects(r As Rectangle) As Rectangle()
        Dim hs = HANDLE_SIZE
        Dim half = hs \ 2
        Return {
            New Rectangle(r.X - half, r.Y - half, hs, hs),                       ' TopLeft
            New Rectangle(r.X + r.Width \ 2 - half, r.Y - half, hs, hs),         ' TopCenter
            New Rectangle(r.Right - half, r.Y - half, hs, hs),                   ' TopRight
            New Rectangle(r.X - half, r.Y + r.Height \ 2 - half, hs, hs),        ' MiddleLeft
            New Rectangle(r.Right - half, r.Y + r.Height \ 2 - half, hs, hs),    ' MiddleRight
            New Rectangle(r.X - half, r.Bottom - half, hs, hs),                  ' BottomLeft
            New Rectangle(r.X + r.Width \ 2 - half, r.Bottom - half, hs, hs),    ' BottomCenter
            New Rectangle(r.Right - half, r.Bottom - half, hs, hs)               ' BottomRight
        }
    End Function

    ' =========================================================================
    ' Mouse interaction
    ' =========================================================================
    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        Me.Focus()
        If e.Button <> MouseButtons.Left Then Return
        Me.Capture = True

        Dim canvasPoint = ScreenToCanvas(e.Location)

        ' If we're placing a new gadget from toolbox
        If _pendingGadgetType.HasValue Then
            _isDrawing = True
            _drawStartPoint = e.Location
            _drawCurrentPoint = e.Location
            Return
        End If

        ' Check if clicking on a resize handle of selected gadget
        If _selectedGadget IsNot Nothing AndAlso Not _selectedGadget.IsLocked Then
            _resizeHandle = HitTestHandles(_selectedGadget, e.Location)
            If _resizeHandle <> ResizeHandleType.None Then
                _isResizing = True
                _dragStart = e.Location
                PushUndo()
                Return
            End If
        End If

        ' Hit test gadgets
        Dim hit = _formDesign.HitTest(canvasPoint)
        If hit IsNot Nothing Then
            ' Ctrl+Click for multi-select
            If (Control.ModifierKeys And Keys.Control) = Keys.Control Then
                If _multiSelection.Contains(hit) Then
                    _multiSelection.Remove(hit)
                    hit.IsSelected = False
                Else
                    _multiSelection.Add(hit)
                    hit.IsSelected = True
                End If
            Else
                _multiSelection.Clear()
                _multiSelection.Add(hit)
            End If

            SelectedGadget = hit
            ' Only allow dragging unlocked gadgets
            If Not hit.IsLocked Then
                _isDragging = True
                _dragStart = e.Location
                _dragOffset = New Point(e.X - CInt(hit.X * _zoom), e.Y - CInt(hit.Y * _zoom))
                PushUndo()
            End If
        Else
            ' Click on empty form surface — start lasso selection
            If Not (Control.ModifierKeys And Keys.Control) = Keys.Control Then
                _formDesign.ClearSelection()
                _multiSelection.Clear()
                SelectedGadget = Nothing
            End If
            _isLassoSelecting = True
            _lassoStart = e.Location
            _lassoCurrent = e.Location
            RaiseEvent FormSurfaceClicked()
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)

        If _isDrawing Then
            _drawCurrentPoint = e.Location
            Invalidate()
            Return
        End If

        ' Lasso selection tracking
        If _isLassoSelecting Then
            _lassoCurrent = e.Location
            Invalidate()
            Return
        End If

        If _isDragging AndAlso _selectedGadget IsNot Nothing Then
            Dim newX = CInt((e.X - _dragOffset.X) / _zoom)
            Dim newY = CInt((e.Y - _dragOffset.Y) / _zoom)

            ' Move delta for multi-selection (smooth — snap only on mouse-up)
            Dim dx = newX - _selectedGadget.X
            Dim dy = newY - _selectedGadget.Y

            For Each sel In _multiSelection
                sel.X += dx
                sel.Y += dy
                ' Clamp to form bounds
                sel.X = Math.Max(0, sel.X)
                sel.Y = Math.Max(0, sel.Y)
            Next

            ' Calculate snap lines during drag
            _snapLines.Clear()
            If _snapLinesEnabled AndAlso _multiSelection.Count = 1 Then
                CalculateSnapLines(_selectedGadget)
            End If

            Invalidate()
            RaiseEvent GadgetMoved(_selectedGadget)
            RaiseEvent DesignChanged()
            Return
        End If

        If _isResizing AndAlso _selectedGadget IsNot Nothing Then
            ApplyResize(e.Location)
            Invalidate()
            RaiseEvent GadgetResized(_selectedGadget)
            RaiseEvent DesignChanged()
            Return
        End If

        ' Update cursor based on hover
        If _selectedGadget IsNot Nothing Then
            Dim handle = HitTestHandles(_selectedGadget, e.Location)
            Me.Cursor = GetResizeCursor(handle)
        ElseIf _pendingGadgetType.HasValue Then
            Me.Cursor = Cursors.Cross
        Else
            Me.Cursor = Cursors.Default
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        Me.Capture = False

        ' Finalize lasso selection
        If _isLassoSelecting Then
            _isLassoSelecting = False
            Dim lassoRect = GetNormalizedRect(_lassoStart, _lassoCurrent)
            ' Only select if user actually dragged (not just a click)
            If lassoRect.Width > 4 OrElse lassoRect.Height > 4 Then
                Dim canvasRect = ScreenToCanvasRect(lassoRect)
                For Each gad In _formDesign.Gadgets
                    Dim gadRect As New Rectangle(gad.X, gad.Y, gad.W, gad.H)
                    If gadRect.IntersectsWith(canvasRect) Then
                        gad.IsSelected = True
                        If Not _multiSelection.Contains(gad) Then _multiSelection.Add(gad)
                    End If
                Next
                If _multiSelection.Count > 0 Then
                    _selectedGadget = _multiSelection.Last()
                    RaiseEvent GadgetSelected(_selectedGadget)
                End If
            End If
            Invalidate()
            Return
        End If

        ' Clear snap lines on release
        _snapLines.Clear()

        If _isDrawing AndAlso _pendingGadgetType.HasValue Then
            _isDrawing = False
            Dim drawRect = GetNormalizedRect(_drawStartPoint, _drawCurrentPoint)
            Dim canvasRect = ScreenToCanvasRect(drawRect)

            ' Enforce minimum size
            If canvasRect.Width < 10 Then canvasRect.Width = W9GadgetRegistry.GetTypeDef(_pendingGadgetType.Value).DefaultWidth
            If canvasRect.Height < 10 Then canvasRect.Height = W9GadgetRegistry.GetTypeDef(_pendingGadgetType.Value).DefaultHeight

            If _snapToGrid Then
                canvasRect.X = SnapToGridValue(canvasRect.X)
                canvasRect.Y = SnapToGridValue(canvasRect.Y)
                canvasRect.Width = SnapToGridValue(canvasRect.Width)
                canvasRect.Height = SnapToGridValue(canvasRect.Height)
            End If

            ' Ensure gadgets are not placed at negative coordinates
            If canvasRect.Y < 0 Then canvasRect.Y = 0

            ' Create the new gadget
            AddGadgetFromToolbox(_pendingGadgetType.Value, canvasRect)
            ClearPendingGadgetType()
            Return
        End If

        _isDragging = False
        _isResizing = False
        _resizeHandle = ResizeHandleType.None

        ' Snap to grid on release (not during drag — keeps movement smooth)
        If _snapToGrid Then
            If _selectedGadget IsNot Nothing Then
                For Each sel In _multiSelection
                    sel.X = SnapToGridValue(sel.X)
                    sel.Y = SnapToGridValue(sel.Y)
                    sel.W = SnapToGridValue(Math.Max(10, sel.W))
                    sel.H = SnapToGridValue(Math.Max(10, sel.H))
                Next
                If Not _multiSelection.Contains(_selectedGadget) Then
                    _selectedGadget.X = SnapToGridValue(_selectedGadget.X)
                    _selectedGadget.Y = SnapToGridValue(_selectedGadget.Y)
                    _selectedGadget.W = SnapToGridValue(Math.Max(10, _selectedGadget.W))
                    _selectedGadget.H = SnapToGridValue(Math.Max(10, _selectedGadget.H))
                End If
                Invalidate()
            End If
        End If
    End Sub

    ' =========================================================================
    ' Drag-and-drop from toolbox
    ' =========================================================================
    Protected Overrides Sub OnDragEnter(e As DragEventArgs)
        If e.Data.GetDataPresent(GetType(W9GadgetType)) Then
            e.Effect = DragDropEffects.Copy
        Else
            e.Effect = DragDropEffects.None
        End If
        MyBase.OnDragEnter(e)
    End Sub

    Protected Overrides Sub OnDragOver(e As DragEventArgs)
        If e.Data.GetDataPresent(GetType(W9GadgetType)) Then
            e.Effect = DragDropEffects.Copy
            Me.Cursor = Cursors.Cross
        End If
        MyBase.OnDragOver(e)
    End Sub

    Protected Overrides Sub OnDragDrop(e As DragEventArgs)
        If e.Data.GetDataPresent(GetType(W9GadgetType)) Then
            Dim gt = DirectCast(e.Data.GetData(GetType(W9GadgetType)), W9GadgetType)
            Dim clientPt = Me.PointToClient(New Point(e.X, e.Y))
            Dim canvasPt = ScreenToCanvas(clientPt)
            Dim tdef = W9GadgetRegistry.GetTypeDef(gt)
            Dim dw = If(tdef IsNot Nothing, tdef.DefaultWidth, 100)
            Dim dh = If(tdef IsNot Nothing, tdef.DefaultHeight, 30)
            Dim rect As New Rectangle(canvasPt.X - dw \ 2, canvasPt.Y - dh \ 2, dw, dh)
            If _snapToGrid Then
                rect.X = SnapToGridValue(rect.X)
                rect.Y = SnapToGridValue(rect.Y)
            End If
            rect.X = Math.Max(0, rect.X)
            rect.Y = Math.Max(0, rect.Y)
            AddGadgetFromToolbox(gt, rect)
        End If
        Me.Cursor = Cursors.Default
        MyBase.OnDragDrop(e)
    End Sub

    ' =========================================================================
    ' Keyboard
    ' =========================================================================
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        MyBase.OnKeyDown(e)

        ' Escape always works — cancel lasso, drawing, or selection
        If e.KeyCode = Keys.Escape Then
            If _isLassoSelecting Then
                _isLassoSelecting = False
                Invalidate()
                e.Handled = True
                Return
            End If
            ClearPendingGadgetType()
            SelectedGadget = Nothing
            _multiSelection.Clear()
            Invalidate()
            e.Handled = True
            Return
        End If

        ' Tab / Shift+Tab: cycle through gadgets
        If e.KeyCode = Keys.Tab AndAlso _formDesign.Gadgets.Count > 0 Then
            Dim idx = If(_selectedGadget IsNot Nothing, _formDesign.Gadgets.IndexOf(_selectedGadget), -1)
            If e.Shift Then
                idx = If(idx <= 0, _formDesign.Gadgets.Count - 1, idx - 1)
            Else
                idx = If(idx >= _formDesign.Gadgets.Count - 1, 0, idx + 1)
            End If
            _multiSelection.Clear()
            _multiSelection.Add(_formDesign.Gadgets(idx))
            SelectedGadget = _formDesign.Gadgets(idx)
            e.Handled = True
            Return
        End If

        ' Home / End: select first/last gadget
        If e.KeyCode = Keys.Home AndAlso _formDesign.Gadgets.Count > 0 Then
            _multiSelection.Clear()
            _multiSelection.Add(_formDesign.Gadgets(0))
            SelectedGadget = _formDesign.Gadgets(0)
            e.Handled = True
            Return
        End If
        If e.KeyCode = Keys.End AndAlso _formDesign.Gadgets.Count > 0 Then
            _multiSelection.Clear()
            Dim last = _formDesign.Gadgets(_formDesign.Gadgets.Count - 1)
            _multiSelection.Add(last)
            SelectedGadget = last
            e.Handled = True
            Return
        End If

        If _selectedGadget Is Nothing Then Return

        ' Ctrl+Shift+Arrow: resize selected gadgets
        If e.Control AndAlso e.Shift Then
            Dim resizeStep = If(_snapToGrid, _gridSize, 1)
            Select Case e.KeyCode
                Case Keys.Right
                    PushUndo()
                    For Each sel In _multiSelection
                        If Not sel.IsLocked Then sel.W += resizeStep
                    Next
                    Invalidate() : RaiseEvent DesignChanged() : e.Handled = True : Return
                Case Keys.Left
                    PushUndo()
                    For Each sel In _multiSelection
                        If Not sel.IsLocked Then sel.W = Math.Max(10, sel.W - resizeStep)
                    Next
                    Invalidate() : RaiseEvent DesignChanged() : e.Handled = True : Return
                Case Keys.Down
                    PushUndo()
                    For Each sel In _multiSelection
                        If Not sel.IsLocked Then sel.H += resizeStep
                    Next
                    Invalidate() : RaiseEvent DesignChanged() : e.Handled = True : Return
                Case Keys.Up
                    PushUndo()
                    For Each sel In _multiSelection
                        If Not sel.IsLocked Then sel.H = Math.Max(10, sel.H - resizeStep)
                    Next
                    Invalidate() : RaiseEvent DesignChanged() : e.Handled = True : Return
            End Select
        End If

        Select Case e.KeyCode
            Case Keys.Delete
                DeleteSelectedGadgets()
                e.Handled = True

            Case Keys.Up
                PushUndo()
                Dim step1 = If(_snapToGrid, _gridSize, 1)
                For Each sel In _multiSelection
                    If Not sel.IsLocked Then sel.Y = Math.Max(0, sel.Y - step1)
                Next
                Invalidate()
                RaiseEvent DesignChanged()
                e.Handled = True

            Case Keys.Down
                PushUndo()
                Dim step2 = If(_snapToGrid, _gridSize, 1)
                For Each sel In _multiSelection
                    If Not sel.IsLocked Then sel.Y += step2
                Next
                Invalidate()
                RaiseEvent DesignChanged()
                e.Handled = True

            Case Keys.Left
                PushUndo()
                Dim step3 = If(_snapToGrid, _gridSize, 1)
                For Each sel In _multiSelection
                    If Not sel.IsLocked Then sel.X = Math.Max(0, sel.X - step3)
                Next
                Invalidate()
                RaiseEvent DesignChanged()
                e.Handled = True

            Case Keys.Right
                PushUndo()
                Dim step4 = If(_snapToGrid, _gridSize, 1)
                For Each sel In _multiSelection
                    If Not sel.IsLocked Then sel.X += step4
                Next
                Invalidate()
                RaiseEvent DesignChanged()
                e.Handled = True
        End Select

        ' Ctrl+C / Ctrl+V / Ctrl+X / Ctrl+D / Ctrl+Z / Ctrl+Y / Ctrl+A
        If e.Control AndAlso Not e.Shift Then
            Select Case e.KeyCode
                Case Keys.C
                    CopySelected()
                    e.Handled = True

                Case Keys.V
                    PasteFromClipboard()
                    e.Handled = True

                Case Keys.X
                    CutSelected()
                    e.Handled = True

                Case Keys.D
                    DuplicateSelected()
                    e.Handled = True

                Case Keys.Z
                    PopUndo()
                    e.Handled = True

                Case Keys.Y
                    PopRedo()
                    e.Handled = True

                Case Keys.A
                    SelectAllGadgets()
                    e.Handled = True
            End Select
        End If
    End Sub

    Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
        Select Case keyData And Keys.KeyCode
            Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Tab
                Return True
            Case Else
                Return MyBase.IsInputKey(keyData)
        End Select
    End Function

    ' =========================================================================
    ' Public methods
    ' =========================================================================

    ''' <summary>Add a new gadget at the specified position.</summary>
    Public Sub AddGadgetFromToolbox(gadgetType As W9GadgetType, rect As Rectangle)
        PushUndo()
        Dim tdef = W9GadgetRegistry.GetTypeDef(gadgetType)
        Dim enumName = GetUniqueEnumName(gadgetType)

        ' Choose default font based on gadget type
        Dim defaultFontName = "Consolas"
        Dim defaultFontSize = 11
        Select Case gadgetType
            Case W9GadgetType.Editor, W9GadgetType.StringInput
                defaultFontName = "Consolas"
                defaultFontSize = 11
            Case W9GadgetType.Button
                defaultFontName = "Segoe UI"
                defaultFontSize = 11
            Case W9GadgetType.TextLabel
                defaultFontName = "Segoe UI"
                defaultFontSize = 11
            Case Else
                defaultFontName = "Segoe UI"
                defaultFontSize = 10
        End Select

        Dim gad As New W9GadgetInstance() With {
            .ID = _formDesign.GetNextGadgetID(),
            .GadgetType = gadgetType,
            .EnumName = enumName,
            .Text = If(tdef IsNot Nothing, tdef.DefaultText, ""),
            .X = rect.X,
            .Y = rect.Y,
            .W = rect.Width,
            .H = rect.Height,
            .ZOrder = _formDesign.Gadgets.Count + 1,
            .FontName = defaultFontName,
            .FontSize = defaultFontSize
        }

        ' Check if placed inside a container (GroupBox, Container, PanelTab)
        Dim container = FindContainerAt(New Point(gad.X + gad.W \ 2, gad.Y + gad.H \ 2), gad)
        If container IsNot Nothing Then
            gad.ParentContainerID = container.ID
        End If

        _formDesign.Gadgets.Add(gad)
        SelectedGadget = gad
        RaiseEvent GadgetAdded(gad)
        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    ''' <summary>Find a container gadget at the given point (for nesting).</summary>
    Private Function FindContainerAt(pt As Point, Optional exclude As W9GadgetInstance = Nothing) As W9GadgetInstance
        ' Search in reverse Z-order for containers
        For i = _formDesign.Gadgets.Count - 1 To 0 Step -1
            Dim g = _formDesign.Gadgets(i)
            If g Is exclude Then Continue For
            Dim tdef = W9GadgetRegistry.GetTypeDef(g.GadgetType)
            If tdef IsNot Nothing AndAlso tdef.IsContainer Then
                Dim containerRect As New Rectangle(g.X, g.Y, g.W, g.H)
                If containerRect.Contains(pt) Then Return g
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>Delete all selected gadgets.</summary>
    Public Sub DeleteSelectedGadgets()
        If _multiSelection.Count = 0 AndAlso _selectedGadget Is Nothing Then Return
        PushUndo()
        If _multiSelection.Count > 0 Then
            For Each sel In _multiSelection
                _formDesign.Gadgets.Remove(sel)
                RaiseEvent GadgetDeleted(sel)
            Next
            _multiSelection.Clear()
        ElseIf _selectedGadget IsNot Nothing Then
            _formDesign.Gadgets.Remove(_selectedGadget)
            RaiseEvent GadgetDeleted(_selectedGadget)
        End If
        _selectedGadget = Nothing
        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    ''' <summary>Refresh after property changes.</summary>
    Public Sub RefreshDesign()
        Invalidate()
        RaiseEvent DesignChanged()
    End Sub

    ' =========================================================================
    ' Undo/Redo
    ' =========================================================================
    Private Sub PushUndo()
        Dim snapshot = _formDesign.Gadgets.Select(Function(g) g.Clone()).ToList()
        _undoStack.Push(snapshot)
        _redoStack.Clear()
        ' Trim stack — keep most recent 50 snapshots
        If _undoStack.Count > 50 Then
            Dim items = _undoStack.ToArray()
            _undoStack.Clear()
            ' Re-push newest 50 (ToArray returns top-of-stack first)
            For i = 49 To 0 Step -1
                _undoStack.Push(items(i))
            Next
        End If
    End Sub

    Private Sub PopUndo()
        If _undoStack.Count = 0 Then Return
        ' Save current for redo
        _redoStack.Push(_formDesign.Gadgets.Select(Function(g) g.Clone()).ToList())
        _formDesign.Gadgets = _undoStack.Pop().ToList()
        _selectedGadget = Nothing
        _multiSelection.Clear()
        Invalidate()
        RaiseEvent DesignChanged()
    End Sub

    Private Sub PopRedo()
        If _redoStack.Count = 0 Then Return
        _undoStack.Push(_formDesign.Gadgets.Select(Function(g) g.Clone()).ToList())
        _formDesign.Gadgets = _redoStack.Pop().ToList()
        _selectedGadget = Nothing
        _multiSelection.Clear()
        Invalidate()
        RaiseEvent DesignChanged()
    End Sub

    ' =========================================================================
    ' Helpers
    ' =========================================================================
    Private Function SnapToGridValue(v As Integer) As Integer
        Return CInt(Math.Round(v / _gridSize) * _gridSize)
    End Function

    ''' <summary>
    ''' Calculate snap lines for the dragged gadget against all other gadgets.
    ''' Adjusts the gadget position when within SNAP_THRESHOLD pixels of alignment.
    ''' </summary>
    Private Sub CalculateSnapLines(gad As W9GadgetInstance)
        _snapLines.Clear()
        Dim bestDx As Integer? = Nothing
        Dim bestDy As Integer? = Nothing

        ' Edges and center of the dragged gadget
        Dim gLeft = gad.X, gRight = gad.X + gad.W, gCenterX = gad.X + gad.W \ 2
        Dim gTop = gad.Y, gBottom = gad.Y + gad.H, gCenterY = gad.Y + gad.H \ 2

        For Each other In _formDesign.Gadgets
            If other Is gad OrElse _multiSelection.Contains(other) Then Continue For

            Dim oLeft = other.X, oRight = other.X + other.W, oCenterX = other.X + other.W \ 2
            Dim oTop = other.Y, oBottom = other.Y + other.H, oCenterY = other.Y + other.H \ 2

            ' Vertical snap lines (X alignment)
            Dim vSnaps = {
                (gLeft, oLeft), (gLeft, oRight), (gLeft, oCenterX),
                (gRight, oLeft), (gRight, oRight), (gRight, oCenterX),
                (gCenterX, oCenterX)
            }
            For Each pair In vSnaps
                Dim diff = pair.Item2 - pair.Item1
                If Math.Abs(diff) <= SNAP_THRESHOLD Then
                    If Not bestDx.HasValue OrElse Math.Abs(diff) < Math.Abs(bestDx.Value) Then
                        bestDx = diff
                    End If
                    Dim lineTop = Math.Min(gTop, oTop) - 5
                    Dim lineBot = Math.Max(gBottom, oBottom) + 5
                    _snapLines.Add(New SnapLineInfo With {
                        .IsHorizontal = False, .Position = pair.Item2,
                        .Start = lineTop, .End = lineBot
                    })
                End If
            Next

            ' Horizontal snap lines (Y alignment)
            Dim hSnaps = {
                (gTop, oTop), (gTop, oBottom), (gTop, oCenterY),
                (gBottom, oTop), (gBottom, oBottom), (gBottom, oCenterY),
                (gCenterY, oCenterY)
            }
            For Each pair In hSnaps
                Dim diff = pair.Item2 - pair.Item1
                If Math.Abs(diff) <= SNAP_THRESHOLD Then
                    If Not bestDy.HasValue OrElse Math.Abs(diff) < Math.Abs(bestDy.Value) Then
                        bestDy = diff
                    End If
                    Dim lineLeft = Math.Min(gLeft, oLeft) - 5
                    Dim lineRight = Math.Max(gRight, oRight) + 5
                    _snapLines.Add(New SnapLineInfo With {
                        .IsHorizontal = True, .Position = pair.Item2,
                        .Start = lineLeft, .End = lineRight
                    })
                End If
            Next
        Next

        ' Apply the best snap offsets
        If bestDx.HasValue Then gad.X += bestDx.Value
        If bestDy.HasValue Then gad.Y += bestDy.Value

        ' Remove snap lines that don't match the final position
        If bestDx.HasValue Then
            _snapLines.RemoveAll(Function(sl)
                                     If sl.IsHorizontal Then Return False
                                     Dim finalLeft = gad.X, finalRight = gad.X + gad.W, finalCX = gad.X + gad.W \ 2
                                     Return sl.Position <> finalLeft AndAlso sl.Position <> finalRight AndAlso sl.Position <> finalCX
                                 End Function)
        End If
        If bestDy.HasValue Then
            _snapLines.RemoveAll(Function(sl)
                                     If Not sl.IsHorizontal Then Return False
                                     Dim finalTop = gad.Y, finalBottom = gad.Y + gad.H, finalCY = gad.Y + gad.H \ 2
                                     Return sl.Position <> finalTop AndAlso sl.Position <> finalBottom AndAlso sl.Position <> finalCY
                                 End Function)
        End If
    End Sub

    Private Function ScreenToCanvas(pt As Point) As Point
        Return New Point(CInt(pt.X / _zoom), CInt(pt.Y / _zoom))
    End Function

    Private Function ScreenToCanvasRect(r As Rectangle) As Rectangle
        Return New Rectangle(CInt(r.X / _zoom), CInt(r.Y / _zoom),
                             CInt(r.Width / _zoom), CInt(r.Height / _zoom))
    End Function

    Private Function GetNormalizedRect(p1 As Point, p2 As Point) As Rectangle
        Return New Rectangle(
            Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y),
            Math.Abs(p2.X - p1.X), Math.Abs(p2.Y - p1.Y))
    End Function

    Private Function HitTestHandles(gad As W9GadgetInstance, pt As Point) As ResizeHandleType
        Dim r As New Rectangle(CInt(gad.X * _zoom), CInt(gad.Y * _zoom),
                               CInt(gad.W * _zoom), CInt(gad.H * _zoom))
        Dim handleRects = GetHandleRects(r)
        Dim types = New ResizeHandleType() {ResizeHandleType.TopLeft, ResizeHandleType.TopCenter, ResizeHandleType.TopRight,
                     ResizeHandleType.MiddleLeft, ResizeHandleType.MiddleRight,
                     ResizeHandleType.BottomLeft, ResizeHandleType.BottomCenter, ResizeHandleType.BottomRight}
        For i = 0 To handleRects.Length - 1
            Dim inflated = Rectangle.Inflate(handleRects(i), 4, 4)
            If inflated.Contains(pt) Then Return types(i)
        Next
        Return ResizeHandleType.None
    End Function

    Private Function GetResizeCursor(handle As ResizeHandleType) As Cursor
        Select Case handle
            Case ResizeHandleType.TopLeft, ResizeHandleType.BottomRight : Return Cursors.SizeNWSE
            Case ResizeHandleType.TopRight, ResizeHandleType.BottomLeft : Return Cursors.SizeNESW
            Case ResizeHandleType.TopCenter, ResizeHandleType.BottomCenter : Return Cursors.SizeNS
            Case ResizeHandleType.MiddleLeft, ResizeHandleType.MiddleRight : Return Cursors.SizeWE
            Case Else
                If _pendingGadgetType.HasValue Then Return Cursors.Cross
                Return Cursors.Default
        End Select
    End Function

    Private Sub ApplyResize(mousePos As Point)
        If _selectedGadget Is Nothing Then Return
        Dim dx = CInt((mousePos.X - _dragStart.X) / _zoom)
        Dim dy = CInt((mousePos.Y - _dragStart.Y) / _zoom)
        _dragStart = mousePos

        Select Case _resizeHandle
            Case ResizeHandleType.TopLeft
                _selectedGadget.X += dx : _selectedGadget.Y += dy
                _selectedGadget.W -= dx : _selectedGadget.H -= dy
            Case ResizeHandleType.TopCenter
                _selectedGadget.Y += dy : _selectedGadget.H -= dy
            Case ResizeHandleType.TopRight
                _selectedGadget.W += dx : _selectedGadget.Y += dy : _selectedGadget.H -= dy
            Case ResizeHandleType.MiddleLeft
                _selectedGadget.X += dx : _selectedGadget.W -= dx
            Case ResizeHandleType.MiddleRight
                _selectedGadget.W += dx
            Case ResizeHandleType.BottomLeft
                _selectedGadget.X += dx : _selectedGadget.W -= dx : _selectedGadget.H += dy
            Case ResizeHandleType.BottomCenter
                _selectedGadget.H += dy
            Case ResizeHandleType.BottomRight
                _selectedGadget.W += dx : _selectedGadget.H += dy
        End Select

        ' Enforce minimums
        If _selectedGadget.W < 10 Then _selectedGadget.W = 10
        If _selectedGadget.H < 10 Then _selectedGadget.H = 10
    End Sub

    ' =========================================================================
    ' Right-Click Context Menu
    ' =========================================================================
    Private _contextMenu As ContextMenuStrip

    Private Sub BuildContextMenu()
        _contextMenu = New ContextMenuStrip()

        Dim mnuCut = _contextMenu.Items.Add("Cut", Nothing, Sub(s, e) CutSelected()) : mnuCut.Name = "mnuCut"
        Dim mnuCopy = _contextMenu.Items.Add("Copy", Nothing, Sub(s, e) CopySelected()) : mnuCopy.Name = "mnuCopy"
        Dim mnuPaste = _contextMenu.Items.Add("Paste", Nothing, Sub(s, e) PasteFromClipboard()) : mnuPaste.Name = "mnuPaste"
        Dim mnuDuplicate = _contextMenu.Items.Add("Duplicate", Nothing, Sub(s, e) DuplicateSelected()) : mnuDuplicate.Name = "mnuDuplicate"
        _contextMenu.Items.Add(New ToolStripSeparator())
        Dim mnuDelete = _contextMenu.Items.Add("Delete", Nothing, Sub(s, e) DeleteSelectedGadgets()) : mnuDelete.Name = "mnuDelete"
        _contextMenu.Items.Add(New ToolStripSeparator())

        ' Z-order
        Dim mnuBringFront = _contextMenu.Items.Add("Bring to Front", Nothing, Sub(s, e) BringToFront_Gadget()) : mnuBringFront.Name = "mnuBringFront"
        Dim mnuSendBack = _contextMenu.Items.Add("Send to Back", Nothing, Sub(s, e) SendToBack_Gadget()) : mnuSendBack.Name = "mnuSendBack"
        _contextMenu.Items.Add(New ToolStripSeparator())

        ' Lock
        Dim mnuLock = _contextMenu.Items.Add("Lock Position", Nothing, Sub(s, e) ToggleLock()) : mnuLock.Name = "mnuLock"
        _contextMenu.Items.Add(New ToolStripSeparator())

        ' Alignment submenu
        Dim mnuAlign = New ToolStripMenuItem("Align")
        mnuAlign.DropDownItems.Add("Align Left", Nothing, Sub(s, e) AlignSelected(AlignDirection.Left))
        mnuAlign.DropDownItems.Add("Align Right", Nothing, Sub(s, e) AlignSelected(AlignDirection.Right))
        mnuAlign.DropDownItems.Add("Align Top", Nothing, Sub(s, e) AlignSelected(AlignDirection.Top))
        mnuAlign.DropDownItems.Add("Align Bottom", Nothing, Sub(s, e) AlignSelected(AlignDirection.Bottom))
        mnuAlign.DropDownItems.Add(New ToolStripSeparator())
        mnuAlign.DropDownItems.Add("Center Horizontally", Nothing, Sub(s, e) AlignSelected(AlignDirection.CenterH))
        mnuAlign.DropDownItems.Add("Center Vertically", Nothing, Sub(s, e) AlignSelected(AlignDirection.CenterV))
        _contextMenu.Items.Add(mnuAlign)

        ' Size submenu
        Dim mnuSize = New ToolStripMenuItem("Make Same Size")
        mnuSize.DropDownItems.Add("Same Width", Nothing, Sub(s, e) SizeSelected(SizeDirection.Width))
        mnuSize.DropDownItems.Add("Same Height", Nothing, Sub(s, e) SizeSelected(SizeDirection.Height))
        mnuSize.DropDownItems.Add("Same Both", Nothing, Sub(s, e) SizeSelected(SizeDirection.Both))
        _contextMenu.Items.Add(mnuSize)

        ' Spacing submenu
        Dim mnuSpace = New ToolStripMenuItem("Space Evenly")
        mnuSpace.DropDownItems.Add("Horizontal Spacing", Nothing, Sub(s, e) SpaceEvenly(True))
        mnuSpace.DropDownItems.Add("Vertical Spacing", Nothing, Sub(s, e) SpaceEvenly(False))
        _contextMenu.Items.Add(mnuSpace)

        _contextMenu.Items.Add(New ToolStripSeparator())

        ' Tab order
        Dim mnuTabOrder = _contextMenu.Items.Add("Show Tab Order", Nothing, Sub(s, e) ToggleTabOrderDisplay()) : mnuTabOrder.Name = "mnuTabOrder"

        ' Select All
        _contextMenu.Items.Add("Select All", Nothing, Sub(s, e) SelectAllGadgets())

        Me.ContextMenuStrip = _contextMenu

        ' Update enabled states when opening
        AddHandler _contextMenu.Opening, Sub(s, e)
                                              Dim hasSelection = _selectedGadget IsNot Nothing
                                              Dim hasMulti = _multiSelection.Count > 1
                                              _contextMenu.Items("mnuCut").Enabled = hasSelection
                                              _contextMenu.Items("mnuCopy").Enabled = hasSelection
                                              _contextMenu.Items("mnuPaste").Enabled = _clipboard IsNot Nothing AndAlso _clipboard.Count > 0
                                              _contextMenu.Items("mnuDuplicate").Enabled = hasSelection
                                              _contextMenu.Items("mnuDelete").Enabled = hasSelection
                                              _contextMenu.Items("mnuBringFront").Enabled = hasSelection
                                              _contextMenu.Items("mnuSendBack").Enabled = hasSelection
                                              mnuAlign.Enabled = hasMulti
                                              mnuSize.Enabled = hasMulti
                                              mnuSpace.Enabled = hasMulti AndAlso _multiSelection.Count >= 3
                                              If hasSelection AndAlso _selectedGadget IsNot Nothing Then
                                                  Dim lockItem = _contextMenu.Items("mnuLock")
                                                  lockItem.Text = If(_selectedGadget.IsLocked, "Unlock Position", "Lock Position")
                                              End If
                                          End Sub
    End Sub

    ' =========================================================================
    ' Copy / Cut / Paste / Duplicate
    ' =========================================================================
    Public Sub CopySelected()
        If _multiSelection.Count > 0 Then
            _clipboard = _multiSelection.Select(Function(g) g.Clone()).ToList()
        ElseIf _selectedGadget IsNot Nothing Then
            _clipboard = New List(Of W9GadgetInstance) From {_selectedGadget.Clone()}
        End If
    End Sub

    Public Sub CutSelected()
        CopySelected()
        If _clipboard IsNot Nothing AndAlso _clipboard.Count > 0 Then DeleteSelectedGadgets()
    End Sub

    Public Sub PasteFromClipboard()
        If _clipboard Is Nothing OrElse _clipboard.Count = 0 Then Return
        PushUndo()
        _multiSelection.Clear()
        _formDesign.ClearSelection()
        For Each src In _clipboard
            Dim pasted = src.Clone()
            pasted.X += 20
            pasted.Y += 20
            pasted.ID = _formDesign.GetNextGadgetID()
            pasted.EnumName = GetUniqueEnumName(pasted.GadgetType)
            pasted.IsSelected = True
            _formDesign.Gadgets.Add(pasted)
            _multiSelection.Add(pasted)
            RaiseEvent GadgetAdded(pasted)
        Next
        If _multiSelection.Count > 0 Then
            _selectedGadget = _multiSelection.Last()
            RaiseEvent GadgetSelected(_selectedGadget)
        End If
        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    Public Sub DuplicateSelected()
        Dim toDuplicate As List(Of W9GadgetInstance) = Nothing
        If _multiSelection.Count > 0 Then
            toDuplicate = _multiSelection.ToList()
        ElseIf _selectedGadget IsNot Nothing Then
            toDuplicate = New List(Of W9GadgetInstance) From {_selectedGadget}
        End If
        If toDuplicate Is Nothing OrElse toDuplicate.Count = 0 Then Return
        PushUndo()
        _multiSelection.Clear()
        _formDesign.ClearSelection()
        For Each src In toDuplicate
            Dim duped = src.Clone()
            duped.X += _gridSize * 2
            duped.Y += _gridSize * 2
            duped.ID = _formDesign.GetNextGadgetID()
            duped.EnumName = GetUniqueEnumName(duped.GadgetType)
            duped.IsSelected = True
            _formDesign.Gadgets.Add(duped)
            _multiSelection.Add(duped)
            RaiseEvent GadgetAdded(duped)
        Next
        If _multiSelection.Count > 0 Then
            _selectedGadget = _multiSelection.Last()
            RaiseEvent GadgetSelected(_selectedGadget)
        End If
        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    ' =========================================================================
    ' Z-Order (Bring to Front / Send to Back)
    ' =========================================================================
    Public Sub BringToFront_Gadget()
        If _selectedGadget Is Nothing Then Return
        PushUndo()
        _formDesign.Gadgets.Remove(_selectedGadget)
        _formDesign.Gadgets.Add(_selectedGadget)
        ReassignZOrder()
        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    Public Sub SendToBack_Gadget()
        If _selectedGadget Is Nothing Then Return
        PushUndo()
        _formDesign.Gadgets.Remove(_selectedGadget)
        _formDesign.Gadgets.Insert(0, _selectedGadget)
        ReassignZOrder()
        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    Private Sub ReassignZOrder()
        For i = 0 To _formDesign.Gadgets.Count - 1
            _formDesign.Gadgets(i).ZOrder = i + 1
        Next
    End Sub

    Private Sub ToggleLock()
        If _selectedGadget Is Nothing Then Return
        _selectedGadget.IsLocked = Not _selectedGadget.IsLocked
        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    ' =========================================================================
    ' Alignment Tools
    ' =========================================================================
    Public Enum AlignDirection
        Left
        Right
        Top
        Bottom
        CenterH
        CenterV
    End Enum

    Public Enum SizeDirection
        Width
        Height
        Both
    End Enum

    Public Sub AlignSelected(direction As AlignDirection)
        If _multiSelection.Count < 2 OrElse _selectedGadget Is Nothing Then Return
        PushUndo()

        ' Use the primary selected gadget as anchor
        Dim anchor = _selectedGadget

        For Each g In _multiSelection
            If g Is anchor Then Continue For
            Select Case direction
                Case AlignDirection.Left : g.X = anchor.X
                Case AlignDirection.Right : g.X = anchor.X + anchor.W - g.W
                Case AlignDirection.Top : g.Y = anchor.Y
                Case AlignDirection.Bottom : g.Y = anchor.Y + anchor.H - g.H
                Case AlignDirection.CenterH : g.X = anchor.X + (anchor.W - g.W) \ 2
                Case AlignDirection.CenterV : g.Y = anchor.Y + (anchor.H - g.H) \ 2
            End Select
        Next

        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    Public Sub SizeSelected(direction As SizeDirection)
        If _multiSelection.Count < 2 OrElse _selectedGadget Is Nothing Then Return
        PushUndo()

        Dim anchor = _selectedGadget

        For Each g In _multiSelection
            If g Is anchor Then Continue For
            Select Case direction
                Case SizeDirection.Width : g.W = anchor.W
                Case SizeDirection.Height : g.H = anchor.H
                Case SizeDirection.Both : g.W = anchor.W : g.H = anchor.H
            End Select
        Next

        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    Public Sub SpaceEvenly(horizontal As Boolean)
        If _multiSelection.Count < 3 Then Return
        PushUndo()

        If horizontal Then
            ' Sort by X position
            Dim sorted = _multiSelection.OrderBy(Function(g) g.X).ToList()
            Dim totalW = sorted.Sum(Function(g) g.W)
            Dim span = (sorted.Last().X + sorted.Last().W) - sorted.First().X
            Dim gap = (span - totalW) / (_multiSelection.Count - 1)
            Dim curX = sorted.First().X
            For Each g In sorted
                g.X = CInt(curX)
                curX += g.W + gap
            Next
        Else
            ' Sort by Y position
            Dim sorted = _multiSelection.OrderBy(Function(g) g.Y).ToList()
            Dim totalH = sorted.Sum(Function(g) g.H)
            Dim span = (sorted.Last().Y + sorted.Last().H) - sorted.First().Y
            Dim gap = (span - totalH) / (_multiSelection.Count - 1)
            Dim curY = sorted.First().Y
            For Each g In sorted
                g.Y = CInt(curY)
                curY += g.H + gap
            Next
        End If

        RaiseEvent DesignChanged()
        Invalidate()
    End Sub

    ' =========================================================================
    ' Tab Order Display
    ' =========================================================================
    Private _showTabOrder As Boolean = False

    Public Sub ToggleTabOrderDisplay()
        _showTabOrder = Not _showTabOrder
        Invalidate()
    End Sub

    Public ReadOnly Property ShowingTabOrder As Boolean
        Get
            Return _showTabOrder
        End Get
    End Property

    Public Sub SelectAllGadgets()
        _multiSelection.Clear()
        For Each gad In _formDesign.Gadgets
            gad.IsSelected = True
            _multiSelection.Add(gad)
        Next
        If _formDesign.Gadgets.Count > 0 Then
            _selectedGadget = _formDesign.Gadgets.Last()
        End If
        RaiseEvent GadgetSelected(_selectedGadget)
        Invalidate()
    End Sub

    ''' <summary>
    ''' Generate a unique enum name across ALL forms in the project (not just the current form).
    ''' This prevents duplicate enum names like giLabel1 appearing in multiple child forms.
    ''' </summary>
    Private Function GetUniqueEnumName(gadgetType As W9GadgetType) As String
        ' Collect all existing enum names across all forms in the project
        Dim existingNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If _project IsNot Nothing Then
            For Each f In _project.Forms
                For Each g In f.Gadgets
                    If Not String.IsNullOrEmpty(g.EnumName) Then
                        existingNames.Add(g.EnumName)
                    End If
                Next
            Next
        Else
            ' Fallback: just check current form
            For Each g In _formDesign.Gadgets
                If Not String.IsNullOrEmpty(g.EnumName) Then
                    existingNames.Add(g.EnumName)
                End If
            Next
        End If
        Return W9GadgetRegistry.GenerateUniqueEnumName(gadgetType, existingNames)
    End Function

End Class
