using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FBEditor.Core;

namespace FBEditor.Avalonia;

/// <summary>
/// Draws red squiggly underlines under misspelled words — but only inside comments
/// (' … and REM) and string literals, so code identifiers are never flagged.
/// Uses an IBackgroundRenderer + DrawingContext (the same proven mechanism as the
/// debug-line renderer) rather than TextDecorations, which don't render in AvaloniaEdit.
/// </summary>
public sealed class SpellCheckRenderer : IBackgroundRenderer
{
    private readonly SpellChecker _spell;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _wholeDocument;
    private static readonly IPen SquigglePen = new Pen(new SolidColorBrush(Color.Parse("#E53935")), 1.2);

    /// <param name="wholeDocument">When true (plain-text files) every word is checked;
    /// when false (code) only comment and string text is checked.</param>
    public SpellCheckRenderer(SpellChecker spell, Func<bool> enabled, Func<bool>? wholeDocument = null)
    {
        _spell = spell;
        _enabled = enabled;
        _wholeDocument = wholeDocument ?? (() => false);
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!_enabled() || !_spell.Enabled || !textView.VisualLinesValid) return;
        var doc = textView.Document;
        if (doc == null) return;

        foreach (var vline in textView.VisualLines)
        {
            var line = vline.FirstDocumentLine;
            var last = vline.LastDocumentLine;
            while (line != null)
            {
                string text = doc.GetText(line);
                foreach (var (s, len) in MisspelledWords(text))
                {
                    var seg = new TextSegment { StartOffset = line.Offset + s, Length = len };
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
                        DrawSquiggle(dc, rect);
                }
                if (line == last) break;
                line = line.NextLine;
            }
        }
    }

    private IEnumerable<(int start, int len)> MisspelledWords(string text)
    {
        var result = new List<(int, int)>();
        // Plain-text files: the whole line is prose. Code files: only comments/strings.
        var spans = _wholeDocument() ? new List<(int, int)> { (0, text.Length) } : CheckableSpans(text);
        foreach (var (spanStart, spanEnd) in spans)
        {
            int i = spanStart;
            while (i < spanEnd && i < text.Length)
            {
                if (char.IsLetter(text[i]))
                {
                    int s = i;
                    while (i < spanEnd && i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'')) i++;
                    string word = text.Substring(s, i - s).Trim('\'');
                    if (word.Length >= 3 && !HasDigit(word))
                    {
                        try { if (!_spell.Check(word)) result.Add((s, word.Length)); }
                        catch { }
                    }
                }
                else i++;
            }
        }
        return result;
    }

    // Spans (start,end) within the line that are comment text or string interiors.
    internal static IEnumerable<(int start, int end)> CheckableSpans(string text)
    {
        var spans = new List<(int, int)>();
        int n = text.Length;

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("REM ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("REM", StringComparison.OrdinalIgnoreCase))
        {
            int idx = text.IndexOf("REM", StringComparison.OrdinalIgnoreCase);
            spans.Add((idx + 3, n));
            return spans;
        }

        int i = 0;
        while (i < n)
        {
            char c = text[i];
            if (c == '\'') { spans.Add((i + 1, n)); break; } // comment to end of line
            if (c == '"')
            {
                int start = i + 1;
                i++;
                while (i < n && text[i] != '"') i++;
                spans.Add((start, Math.Min(i, n)));
                if (i < n) i++; // skip closing quote
                continue;
            }
            i++;
        }
        return spans;
    }

    private static bool HasDigit(string w)
    {
        foreach (var c in w) if (char.IsDigit(c)) return true;
        return false;
    }

    private static void DrawSquiggle(DrawingContext dc, Rect rect)
    {
        double y = rect.Bottom - 1;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            double x = rect.Left;
            ctx.BeginFigure(new Point(x, y), false);
            bool up = true;
            while (x < rect.Right)
            {
                x = Math.Min(x + 3, rect.Right);
                ctx.LineTo(new Point(x, up ? y - 2.5 : y));
                up = !up;
            }
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, SquigglePen, geo);
    }
}
