using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using FBEditor.Core;

namespace FBEditor.Avalonia;

/// <summary>
/// Underlines misspelled words in red — but only inside comments (' … and REM) and
/// string literals, so code identifiers are never flagged. Driven by Core.SpellChecker;
/// does nothing if spell-check is off or no dictionary is loaded.
/// </summary>
public sealed class SpellCheckColorizer : DocumentColorizingTransformer
{
    private readonly SpellChecker _spell;
    private readonly Func<bool> _enabled;
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#E53935"));

    public SpellCheckColorizer(SpellChecker spell, Func<bool> enabled)
    {
        _spell = spell;
        _enabled = enabled;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0 || !_enabled() || !_spell.Enabled) return;
        try { Run(line); }
        catch { /* never let spell-check break rendering */ }
    }

    private void Run(DocumentLine line)
    {
        int lineStart = line.Offset;
        string text = CurrentContext.Document.GetText(line);
        foreach (var (start, end) in CheckableSpans(text))
            CheckWords(text, start, end, lineStart);
    }

    // Spans (start,end) within the line text that are comment text or string interiors.
    private static IEnumerable<(int start, int end)> CheckableSpans(string text)
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
                int s = i + 1;
                i++;
                while (i < n && text[i] != '"') i++;
                spans.Add((s, Math.Min(i, n)));
                if (i < n) i++; // skip closing quote
                continue;
            }
            i++;
        }
        return spans;
    }

    private void CheckWords(string text, int spanStart, int spanEnd, int lineStart)
    {
        int i = spanStart;
        while (i < spanEnd && i < text.Length)
        {
            if (char.IsLetter(text[i]))
            {
                int s = i;
                while (i < spanEnd && i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'')) i++;
                string word = text.Substring(s, i - s).Trim('\'');
                // Skip short words and anything with digits/caps-mixed (likely identifiers).
                if (word.Length >= 3 && !HasDigit(word) && !_spell.Check(word))
                    Underline(lineStart + s, lineStart + s + word.Length);
            }
            else i++;
        }
    }

    private static bool HasDigit(string w)
    {
        foreach (var c in w) if (char.IsDigit(c)) return true;
        return false;
    }

    private void Underline(int start, int end)
    {
        if (end <= start) return;
        var dec = new TextDecorationCollection
        {
            new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Stroke = Red,
                StrokeThickness = 1
            }
        };
        ChangeLinePart(start, end, el => el.TextRunProperties.SetTextDecorations(dec));
    }
}
