using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FBEditor.Core;

namespace FBEditor.Avalonia;

/// <summary>
/// Single-pass FreeBASIC syntax colorizer for AvaloniaEdit. The keyword / type /
/// function word lists come straight from FBEditor.Core.SyntaxConfig, so the editor
/// and the engine stay in sync. Colors use the familiar VS Code "Dark+" palette.
///
/// v1 handles: line comments (' and REM), string literals, #preprocessor directives,
/// numbers, and keyword/type/function words. Multi-line /' '/ block comments are
/// not yet tracked across lines (single-line ones are left alone) — a later pass.
/// </summary>
public sealed class FreeBasicColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush CommentBrush = new ImmutableSolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush StringBrush  = new ImmutableSolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush KeywordBrush = new ImmutableSolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush TypeBrush    = new ImmutableSolidColorBrush(Color.Parse("#4EC9B0"));
    private static readonly IBrush FuncBrush    = new ImmutableSolidColorBrush(Color.Parse("#DCDCAA"));
    private static readonly IBrush PreprocBrush = new ImmutableSolidColorBrush(Color.Parse("#C586C0"));
    private static readonly IBrush NumberBrush  = new ImmutableSolidColorBrush(Color.Parse("#B5CEA8"));

    private static readonly HashSet<string> Keywords = BuildSet(SyntaxConfig.FB_KEYWORDS);
    private static readonly HashSet<string> Types    = BuildSet(SyntaxConfig.FB_TYPES);
    private static readonly HashSet<string> Funcs    = BuildSet(SyntaxConfig.FB_FUNCTIONS);

    private static HashSet<string> BuildSet(string words)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            set.Add(w);
        return set;
    }

    private readonly Func<bool> _enabled;

    /// <param name="enabled">Highlighting is applied only when this returns true
    /// (e.g. for code files, but not plain-text files).</param>
    public FreeBasicColorizer(Func<bool>? enabled = null)
    {
        _enabled = enabled ?? (() => true);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0 || !_enabled()) return;
        try
        {
            ColorizeLineCore(line);
        }
        catch
        {
            // A highlighting glitch must never break text rendering — fail silent.
        }
    }

    private void ColorizeLineCore(DocumentLine line)
    {
        int lineStart = line.Offset;
        string text = CurrentContext.Document.GetText(line);
        int n = text.Length;
        int i = 0;

        while (i < n)
        {
            char c = text[i];

            // ' line comment -> to end of line
            if (c == '\'')
            {
                Apply(lineStart + i, lineStart + n, CommentBrush);
                return;
            }

            // String literal
            if (c == '"')
            {
                int start = i;
                i++;
                while (i < n && text[i] != '"') i++;
                if (i < n) i++; // include closing quote
                Apply(lineStart + start, lineStart + i, StringBrush);
                continue;
            }

            // Preprocessor directive: # plus the directive word
            if (c == '#')
            {
                int start = i;
                i++;
                while (i < n && (char.IsLetter(text[i]))) i++;
                Apply(lineStart + start, lineStart + i, PreprocBrush);
                continue;
            }

            // Number
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '.')) i++;
                Apply(lineStart + start, lineStart + i, NumberBrush);
                continue;
            }

            // Identifier / keyword
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                string word = text.Substring(start, i - start);

                // "REM" at the start of the line (ignoring leading whitespace) is a comment.
                if (string.Equals(word, "REM", StringComparison.OrdinalIgnoreCase) &&
                    text.Substring(0, start).Trim().Length == 0)
                {
                    Apply(lineStart + start, lineStart + n, CommentBrush);
                    return;
                }

                if (Keywords.Contains(word)) Apply(lineStart + start, lineStart + i, KeywordBrush);
                else if (Types.Contains(word)) Apply(lineStart + start, lineStart + i, TypeBrush);
                else if (Funcs.Contains(word)) Apply(lineStart + start, lineStart + i, FuncBrush);
                continue;
            }

            i++;
        }
    }

    private void Apply(int startOffset, int endOffset, IBrush brush)
    {
        if (endOffset <= startOffset) return;
        ChangeLinePart(startOffset, endOffset,
            el => el.TextRunProperties.SetForegroundBrush(brush));
    }
}
