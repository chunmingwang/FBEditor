Imports ScintillaNET
Imports NHunspell
Imports System.IO
Imports System.Drawing
Imports System.Text
Imports System.Text.RegularExpressions

''' <summary>
''' Spell checking engine for FBEditor.
''' Uses NHunspell with Hunspell dictionaries to check spelling
''' in comments, string literals, and text files.
''' </summary>
Public Module SpellChecker

    Public Const INDICATOR_SPELLING As Integer = 8

    ' FreeBasic style IDs for spellcheckable regions
    Private ReadOnly SPELL_STYLES_CODE As Integer() = {
        Style.FreeBasic.Comment,
        Style.FreeBasic.CommentBlock,
        Style.FreeBasic.String,
        Style.FreeBasic.StringEol
    }

    ' Text file extensions - spellcheck entire content
    Private ReadOnly TEXT_EXTENSIONS As String() = {
        ".txt", ".md", ".log", ".ini", ".cfg",
        ".readme", ".text", ".markdown"
    }

    Private _hunspell As Hunspell = Nothing
    Private _customWords As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private _customDicPath As String = ""
    Private _initialized As Boolean = False

    ''' <summary>
    ''' Status message from last initialization attempt, visible in status bar.
    ''' </summary>
    Public InitStatus As String = ""

    Private ReadOnly WordPattern As New Regex(
        "\b[A-Za-z']{2,}\b",
        RegexOptions.Compiled)

    ''' <summary>
    ''' Initialize the spell checker with dictionary files.
    ''' </summary>
    Public Sub Initialize(appPath As String, settingsPath As String)
        If _initialized Then Return
        Try
            Dim affPath = Path.Combine(appPath, "Resources", "Dictionaries", "en_US.aff")
            Dim dicPath = Path.Combine(appPath, "Resources", "Dictionaries", "en_US.dic")

            If Not File.Exists(affPath) OrElse Not File.Exists(dicPath) Then
                InitStatus = "Spell Check: dictionary files not found"
                DiagnosticsLogger.LogWarning("SpellChecker",
                    "Dictionary files not found at: " & affPath)
                Return
            End If

            _hunspell = New Hunspell(affPath, dicPath)
            _initialized = True

            _customDicPath = Path.Combine(settingsPath, "custom.dic")
            LoadCustomDictionary()

            InitStatus = "Spell Check: ready"
            DiagnosticsLogger.LogInfo("SpellChecker", "Initialized successfully.")
        Catch ex As Exception
            InitStatus = "Spell Check: init failed - " & ex.Message
            DiagnosticsLogger.LogError("SpellChecker", "Failed to initialize", ex)
            _initialized = False
        End Try
    End Sub

    ''' <summary>
    ''' Dispose the Hunspell engine.
    ''' </summary>
    Public Sub Shutdown()
        If _hunspell IsNot Nothing Then
            _hunspell.Dispose()
            _hunspell = Nothing
        End If
        _initialized = False
    End Sub

    Public ReadOnly Property IsReady As Boolean
        Get
            Return _initialized AndAlso _hunspell IsNot Nothing
        End Get
    End Property

    ''' <summary>
    ''' Set up the spelling indicator on the Scintilla control.
    ''' </summary>
    Public Sub SetupIndicator(sci As Scintilla)
        sci.Indicators(INDICATOR_SPELLING).Style = IndicatorStyle.Squiggle
        sci.Indicators(INDICATOR_SPELLING).ForeColor = Color.Red
        sci.Indicators(INDICATOR_SPELLING).Under = True
        sci.Indicators(INDICATOR_SPELLING).OutlineAlpha = 255
        sci.Indicators(INDICATOR_SPELLING).Alpha = 255
    End Sub

    ''' <summary>
    ''' Check whether a given file should be treated as a plain text file.
    ''' </summary>
    Public Function IsTextFile(fileName As String) As Boolean
        If String.IsNullOrEmpty(fileName) Then Return False
        Dim ext = Path.GetExtension(fileName).ToLowerInvariant()
        Return TEXT_EXTENSIONS.Contains(ext)
    End Function

    Private Function IsSpellCheckableStyle(sci As Scintilla, position As Integer) As Boolean
        Dim style = sci.GetStyleAt(position)
        Return SPELL_STYLES_CODE.Contains(style)
    End Function

    ''' <summary>
    ''' Spellcheck visible lines and apply indicators.
    ''' </summary>
    Public Sub CheckVisibleLines(sci As Scintilla, fileName As String)
        If Not _initialized OrElse _hunspell Is Nothing Then Return
        If sci Is Nothing OrElse sci.TextLength = 0 Then Return

        Dim firstLine = sci.FirstVisibleLine
        Dim linesOnScreen = sci.LinesOnScreen
        Dim lastLine = Math.Min(firstLine + linesOnScreen + 1, sci.Lines.Count - 1)

        ' Clamp to valid range
        If firstLine < 0 Then firstLine = 0
        If lastLine < firstLine Then Return
        If lastLine >= sci.Lines.Count Then lastLine = sci.Lines.Count - 1

        Dim startPos = sci.Lines(firstLine).Position
        Dim endPos = sci.Lines(lastLine).EndPosition
        Dim rangeLen = endPos - startPos
        If rangeLen <= 0 Then Return

        ' Force the lexer to style the visible range before checking styles
        sci.Colorize(startPos, endPos)

        ' Clear existing spelling indicators in visible range
        sci.IndicatorCurrent = INDICATOR_SPELLING
        sci.IndicatorClearRange(startPos, rangeLen)

        Dim isText = IsTextFile(fileName)

        For lineIdx = firstLine To lastLine
            Dim line = sci.Lines(lineIdx)
            Dim lineText = line.Text
            Dim lineStart = line.Position

            For Each m As Match In WordPattern.Matches(lineText)
                Dim word = m.Value

                ' Calculate byte offset for Scintilla (uses UTF-8 byte positions)
                Dim byteOffset = Encoding.UTF8.GetByteCount(lineText, 0, m.Index)
                Dim wordPos = lineStart + byteOffset
                Dim wordByteLen = Encoding.UTF8.GetByteCount(word)

                ' Skip if not in a spellcheckable region (unless text file)
                If Not isText Then
                    If Not IsSpellCheckableStyle(sci, wordPos) Then
                        Continue For
                    End If
                End If

                ' Skip short all-caps words (acronyms like API, GUI, DLL)
                If word.Length <= 4 AndAlso word = word.ToUpperInvariant() Then
                    Continue For
                End If

                If Not CheckWord(word) Then
                    sci.IndicatorCurrent = INDICATOR_SPELLING
                    sci.IndicatorFillRange(wordPos, wordByteLen)
                End If
            Next
        Next
    End Sub

    ''' <summary>
    ''' Check a single word against the dictionary and custom word list.
    ''' </summary>
    Public Function CheckWord(word As String) As Boolean
        If String.IsNullOrEmpty(word) OrElse word.Length < 2 Then Return True

        Dim cleaned = word.Trim("'"c)
        If cleaned.Length < 2 Then Return True

        ' Check custom dictionary first
        If _customWords.Contains(cleaned) Then Return True

        Try
            Return _hunspell.Spell(cleaned)
        Catch
            Return True
        End Try
    End Function

    ''' <summary>
    ''' Get spelling suggestions for a misspelled word.
    ''' </summary>
    Public Function GetSuggestions(word As String) As List(Of String)
        If Not _initialized OrElse _hunspell Is Nothing Then
            Return New List(Of String)()
        End If
        Try
            Return _hunspell.Suggest(word)
        Catch
            Return New List(Of String)()
        End Try
    End Function

    ''' <summary>
    ''' Get the misspelled word at the given position, or empty string if none.
    ''' </summary>
    Public Function GetMisspelledWordAt(sci As Scintilla, position As Integer) As String
        If position < 0 OrElse position >= sci.TextLength Then Return ""

        ' Check if position has the spelling indicator
        Dim bitmask = sci.IndicatorAllOnFor(position)
        If (bitmask And (1 << INDICATOR_SPELLING)) = 0 Then
            Return ""
        End If

        Dim wordStart = sci.WordStartPosition(position, True)
        Dim wordEnd = sci.WordEndPosition(position, True)
        If wordEnd <= wordStart Then Return ""

        Return sci.GetTextRange(wordStart, wordEnd - wordStart)
    End Function

    ''' <summary>
    ''' Add a word to the custom dictionary.
    ''' </summary>
    Public Sub AddToCustomDictionary(word As String)
        If String.IsNullOrEmpty(word) Then Return
        _customWords.Add(word)
        SaveCustomDictionary()
    End Sub

    ''' <summary>
    ''' Clear all spelling indicators from the document.
    ''' </summary>
    Public Sub ClearAllIndicators(sci As Scintilla)
        If sci Is Nothing OrElse sci.TextLength = 0 Then Return
        sci.IndicatorCurrent = INDICATOR_SPELLING
        sci.IndicatorClearRange(0, sci.TextLength)
    End Sub

    Private Sub LoadCustomDictionary()
        _customWords.Clear()
        If String.IsNullOrEmpty(_customDicPath) Then Return
        If Not File.Exists(_customDicPath) Then Return
        Try
            For Each line In File.ReadAllLines(_customDicPath)
                Dim w = line.Trim()
                If w.Length > 0 Then _customWords.Add(w)
            Next
        Catch ex As Exception
            DiagnosticsLogger.LogError("SpellChecker", "Failed to load custom dictionary", ex)
        End Try
    End Sub

    Private Sub SaveCustomDictionary()
        If String.IsNullOrEmpty(_customDicPath) Then Return
        Try
            File.WriteAllLines(_customDicPath, _customWords.ToArray())
        Catch ex As Exception
            DiagnosticsLogger.LogError("SpellChecker", "Failed to save custom dictionary", ex)
        End Try
    End Sub
End Module
