using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace FBEditor.Avalonia;

/// <summary>
/// Computes foldable regions for FreeBASIC: block constructs (Sub/Function/Property/
/// Constructor/Destructor/Type/Enum/Union/Namespace/Scope ... End X) and multi-line
/// /' '/ block comments. v1 pairs openers to "End &lt;kw&gt;" with a simple stack.
/// </summary>
public sealed class FreeBasicFoldingStrategy
{
    private static readonly HashSet<string> Openers = new(StringComparer.OrdinalIgnoreCase)
    {
        "sub", "function", "property", "constructor", "destructor",
        "type", "enum", "union", "namespace", "scope"
    };

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateFoldings(document)
            .OrderBy(f => f.StartOffset)
            .ToList();
        manager.UpdateFoldings(foldings, -1);
    }

    private static IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        var result = new List<NewFolding>();

        // --- Block constructs via a keyword stack ---
        var stack = new Stack<int>(); // start offsets (end of opener line)
        foreach (var line in document.Lines)
        {
            var raw = document.GetText(line);
            var t = raw.TrimStart();
            var lower = t.ToLowerInvariant();

            if (lower.StartsWith("end ") || lower == "end")
            {
                var second = FirstWord(lower.Length > 3 ? lower.Substring(4) : "");
                if (Openers.Contains(second) && stack.Count > 0)
                {
                    int start = stack.Pop();
                    if (line.EndOffset > start)
                        result.Add(new NewFolding(start, line.EndOffset) { Name = "..." });
                }
                continue;
            }

            var first = FirstWord(lower);
            if (first == "declare") continue;            // prototype, not a block
            if (Openers.Contains(first))
                stack.Push(line.EndOffset);              // fold the body after the opener line
        }

        // --- Multi-line /' '/ block comments ---
        string text = document.Text;
        int idx = 0;
        while ((idx = text.IndexOf("/'", idx, StringComparison.Ordinal)) >= 0)
        {
            int end = text.IndexOf("'/", idx + 2, StringComparison.Ordinal);
            if (end < 0) break;
            int closeEnd = end + 2;
            if (document.GetLineByOffset(idx).LineNumber != document.GetLineByOffset(end).LineNumber)
                result.Add(new NewFolding(idx, closeEnd) { Name = "/' ... '/" });
            idx = closeEnd;
        }

        return result;
    }

    private static string FirstWord(string s)
    {
        s = s.TrimStart();
        int i = 0;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '#')) i++;
        return s.Substring(0, i);
    }
}
