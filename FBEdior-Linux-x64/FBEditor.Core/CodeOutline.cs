using System.Text.RegularExpressions;

namespace FBEditor.Core;

public enum OutlineItemType
{
    TypeDef = 1,
    EnumDef = 2,
    ConstDef = 3,
    Variable = 4,
    ArrayDef = 5,
    SubDef = 6,
    FuncDef = 7,
    PropertyDef = 8,
    DeclareDef = 9,
    GlobalVar = 10,
    GlobalArray = 11,
    DynArray = 12
}

public class OutlineItem
{
    public OutlineItemType ItemType;
    public string Name = "";
    public int LineNumber = 0;
    public string Signature = "";
    public string DataType = "";

    public string Category => ItemType switch
    {
        OutlineItemType.SubDef or OutlineItemType.FuncDef => "Procedures",
        OutlineItemType.TypeDef => "Types",
        OutlineItemType.EnumDef => "Enums",
        OutlineItemType.ConstDef => "Constants",
        OutlineItemType.GlobalVar => "Global Variables",
        OutlineItemType.GlobalArray or OutlineItemType.DynArray => "Global Arrays",
        OutlineItemType.Variable => "Variables",
        OutlineItemType.ArrayDef => "Arrays",
        OutlineItemType.PropertyDef => "Properties",
        OutlineItemType.DeclareDef => "Declares",
        _ => "Other"
    };

    public string Icon => ItemType switch
    {
        OutlineItemType.SubDef => "S",
        OutlineItemType.FuncDef => "F",
        OutlineItemType.TypeDef => "T",
        OutlineItemType.EnumDef => "E",
        OutlineItemType.ConstDef => "C",
        OutlineItemType.Variable => "V",
        OutlineItemType.ArrayDef => "A",
        OutlineItemType.GlobalVar => "G",
        OutlineItemType.GlobalArray => "GA",
        OutlineItemType.DynArray => "DA",
        OutlineItemType.PropertyDef => "P",
        OutlineItemType.DeclareDef => "D",
        _ => "?"
    };
}

/// <summary>
/// Parses FreeBASIC source into a structure outline (Types, Enums, Subs,
/// Functions, Consts, variables, etc.). Pure regex/text analysis.
/// Ported from Modules/CodeOutline.vb (VB Module -> C# static class).
/// </summary>
public static class CodeOutline
{
    // Split on `sep` but only at parenthesis depth 0, so commas inside an
    // initializer's call args (e.g. "x = f(a, b)") don't create phantom
    // variables. Fixes a latent bug in the VB original where
    // "Dim txt As String = GetListBoxText(g, idx)" produced a bogus "idx".
    private static IEnumerable<string> SplitTopLevel(string s, char sep)
    {
        int depth = 0, start = 0;
        for (int k = 0; k < s.Length; k++)
        {
            char c = s[k];
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == sep && depth == 0)
            {
                yield return s.Substring(start, k - start);
                start = k + 1;
            }
        }
        yield return s.Substring(start);
    }

    public static List<OutlineItem> ParseOutline(string code)
    {
        var items = new List<OutlineItem>();
        if (string.IsNullOrEmpty(code)) return items;

        var lines = code.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        bool inType = false, inEnum = false, inMultiLineComment = false;
        bool inProc = false; // Track if inside Sub/Function/Property/Constructor/Destructor
        string currentTypeName = "";

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var upper = line.ToUpper();
            if (line.Length == 0) continue;
            if (line.StartsWith("'") || upper.StartsWith("REM ")) continue;

            // Multi-line comments
            if (upper.StartsWith("/'")) { inMultiLineComment = true; continue; }
            if (inMultiLineComment)
            {
                if (line.Contains("'/")) inMultiLineComment = false;
                continue;
            }

            // Track Type/Enum blocks
            if (Regex.IsMatch(upper, @"^\s*TYPE\s+\w+") && !upper.Contains(" AS "))
            {
                var m = Regex.Match(line, @"(?i)type\s+(\w+)");
                if (m.Success)
                {
                    currentTypeName = m.Groups[1].Value;
                    items.Add(new OutlineItem
                    {
                        ItemType = OutlineItemType.TypeDef,
                        Name = currentTypeName,
                        LineNumber = i + 1
                    });
                }
                inType = true;
                continue;
            }
            if (upper.TrimStart().StartsWith("END TYPE"))
            {
                inType = false;
                currentTypeName = "";
                continue;
            }

            // Inside TYPE: parse DECLARE statements and skip field declarations
            if (inType)
            {
                var typeDeclMatch = Regex.Match(line,
                    @"(?i)^\s*declare\s+(static\s+)?(sub|function|constructor|destructor|property|operator)\s*([\w.]*)\s*(\(.*)?");
                if (typeDeclMatch.Success)
                {
                    var kind = typeDeclMatch.Groups[2].Value.ToUpper();
                    var mName = typeDeclMatch.Groups[3].Value;
                    var fullName = currentTypeName.Length > 0 && mName.Length > 0
                        ? currentTypeName + "." + mName
                        : (mName.Length > 0 ? mName : currentTypeName + "." + kind.ToLower());
                    // All TYPE-internal declares map to DeclareDef (kind preserved in signature).
                    items.Add(new OutlineItem
                    {
                        ItemType = OutlineItemType.DeclareDef,
                        Name = fullName,
                        LineNumber = i + 1,
                        Signature = line.Trim()
                    });
                }
                continue;
            }

            if (Regex.IsMatch(upper, @"^\s*ENUM\s+\w+"))
            {
                var m = Regex.Match(line, @"(?i)enum\s+(\w+)");
                if (m.Success)
                {
                    items.Add(new OutlineItem
                    {
                        ItemType = OutlineItemType.EnumDef,
                        Name = m.Groups[1].Value,
                        LineNumber = i + 1
                    });
                }
                inEnum = true;
                continue;
            }
            if (upper.TrimStart().StartsWith("END ENUM")) { inEnum = false; continue; }
            if (inEnum) continue;

            // Track procedure scope (END SUB/FUNCTION/PROPERTY/CONSTRUCTOR/DESTRUCTOR)
            if (upper.StartsWith("END SUB") || upper.StartsWith("END FUNCTION") ||
                upper.StartsWith("END PROPERTY") || upper.StartsWith("END CONSTRUCTOR") ||
                upper.StartsWith("END DESTRUCTOR") || upper.StartsWith("END OPERATOR"))
            {
                inProc = false;
                continue;
            }

            // CONSTRUCTOR (standalone implementation)
            var ctorMatch = Regex.Match(line, @"(?i)^constructor\s+([\w.]+)\s*(\(.*)?");
            if (ctorMatch.Success)
            {
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.SubDef,
                    Name = ctorMatch.Groups[1].Value + ".constructor",
                    LineNumber = i + 1,
                    Signature = line
                });
                inProc = true;
                continue;
            }

            // DESTRUCTOR (standalone implementation)
            var dtorMatch = Regex.Match(line, @"(?i)^destructor\s+([\w.]+)\s*(\(.*)?");
            if (dtorMatch.Success)
            {
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.SubDef,
                    Name = dtorMatch.Groups[1].Value + ".destructor",
                    LineNumber = i + 1,
                    Signature = line
                });
                inProc = true;
                continue;
            }

            // OPERATOR (standalone implementation)
            var operMatch = Regex.Match(line, @"(?i)^operator\s+([\w.]+)\s*(\(.*)?");
            if (operMatch.Success)
            {
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.SubDef,
                    Name = operMatch.Groups[1].Value,
                    LineNumber = i + 1,
                    Signature = line
                });
                inProc = true;
                continue;
            }

            // SUB (supports ClassName.MethodName dot notation)
            var subMatch = Regex.Match(line, @"(?i)^(public\s+|private\s+|static\s+)*sub\s+([\w.]+)\s*(\(.*)?$");
            if (subMatch.Success)
            {
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.SubDef,
                    Name = subMatch.Groups[2].Value,
                    LineNumber = i + 1,
                    Signature = line
                });
                inProc = true;
                continue;
            }

            // FUNCTION (supports ClassName.MethodName dot notation)
            var funcMatch = Regex.Match(line, @"(?i)^(public\s+|private\s+|static\s+)*function\s+([\w.]+)\s*(\(.*)?");
            if (funcMatch.Success)
            {
                string dt = "";
                int lastParen = line.LastIndexOf(')');
                if (lastParen >= 0 && lastParen < line.Length - 1)
                {
                    string afterParen = line.Substring(lastParen + 1);
                    var asMatch = Regex.Match(afterParen, @"(?i)^\s*as\s+(\w+)");
                    if (asMatch.Success) dt = asMatch.Groups[1].Value;
                }
                if (dt.Length == 0 && lastParen < 0)
                {
                    var noParenMatch = Regex.Match(line, @"(?i)function\s+[\w.]+\s+as\s+(\w+)");
                    if (noParenMatch.Success) dt = noParenMatch.Groups[1].Value;
                }
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.FuncDef,
                    Name = funcMatch.Groups[2].Value,
                    LineNumber = i + 1,
                    Signature = line,
                    DataType = dt
                });
                inProc = true;
                continue;
            }

            // PROPERTY (supports ClassName.PropertyName dot notation)
            var propMatch = Regex.Match(line, @"(?i)^(public\s+|private\s+)*property\s+([\w.]+)");
            if (propMatch.Success)
            {
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.PropertyDef,
                    Name = propMatch.Groups[2].Value,
                    LineNumber = i + 1
                });
                inProc = true;
                continue;
            }

            // CONST
            if (upper.StartsWith("CONST "))
            {
                string constLine = line.Substring(6).Trim();
                string constUpper = constLine.ToUpper();

                // Pattern: CONST AS <type> name1 = val, name2 = val
                if (constUpper.StartsWith("AS "))
                {
                    string afterAs = constLine.Substring(3).Trim();
                    int spacePos = afterAs.IndexOf(' ');
                    if (spacePos > 0)
                    {
                        string varList = afterAs.Substring(spacePos + 1).Trim();
                        foreach (var cPart in SplitTopLevel(varList, ','))
                        {
                            string cName = cPart.Trim();
                            int eqPos = cName.IndexOf('=');
                            if (eqPos > 0) cName = cName.Substring(0, eqPos).Trim();
                            if (cName.Length > 0 && Regex.IsMatch(cName, @"^\w+$"))
                            {
                                items.Add(new OutlineItem
                                {
                                    ItemType = OutlineItemType.ConstDef,
                                    Name = cName,
                                    LineNumber = i + 1
                                });
                            }
                        }
                    }
                }
                else
                {
                    // Pattern: CONST name1 = val, name2 AS type = val
                    foreach (var cPart in SplitTopLevel(constLine, ','))
                    {
                        var cMatch = Regex.Match(cPart.Trim(), @"(?i)^(\w+)");
                        if (cMatch.Success)
                        {
                            string cName = cMatch.Groups[1].Value;
                            if (cName.ToUpper() != "AS")
                            {
                                items.Add(new OutlineItem
                                {
                                    ItemType = OutlineItemType.ConstDef,
                                    Name = cName,
                                    LineNumber = i + 1
                                });
                            }
                        }
                    }
                }
                continue;
            }

            // #DEFINE
            var defineMatch = Regex.Match(line, @"(?i)^#define\s+(\w+)");
            if (defineMatch.Success)
            {
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.ConstDef,
                    Name = defineMatch.Groups[1].Value,
                    LineNumber = i + 1
                });
                continue;
            }

            // DECLARE (standalone, outside TYPE)
            var declMatch = Regex.Match(line, @"(?i)^declare\s+(sub|function|constructor|destructor|property|operator)\s+([\w.]+)");
            if (declMatch.Success)
            {
                items.Add(new OutlineItem
                {
                    ItemType = OutlineItemType.DeclareDef,
                    Name = declMatch.Groups[2].Value,
                    LineNumber = i + 1,
                    Signature = line
                });
                continue;
            }

            // DIM / REDIM / COMMON / STATIC variable declarations
            // Robust keyword parsing instead of complex regex alternation.
            string? dimLine = null;
            bool isShared = false;
            bool isRedim = false;

            if (upper.StartsWith("DIM SHARED ") || upper.StartsWith("DIM\tSHARED"))
            {
                dimLine = line.Substring(line.ToUpper().IndexOf("SHARED") + 6).Trim();
                isShared = true;
            }
            else if (upper.StartsWith("COMMON SHARED "))
            {
                dimLine = line.Substring(line.ToUpper().IndexOf("SHARED") + 6).Trim();
                isShared = true;
            }
            else if (upper.StartsWith("REDIM SHARED "))
            {
                dimLine = line.Substring(line.ToUpper().IndexOf("SHARED") + 6).Trim();
                isShared = true; isRedim = true;
            }
            else if (upper.StartsWith("REDIM PRESERVE "))
            {
                dimLine = line.Substring(15).Trim();
                isRedim = true;
            }
            else if (upper.StartsWith("REDIM "))
            {
                dimLine = line.Substring(6).Trim();
                isRedim = true;
            }
            else if (upper.StartsWith("DIM "))
            {
                dimLine = line.Substring(4).Trim();
            }
            else if (upper.StartsWith("COMMON "))
            {
                dimLine = line.Substring(7).Trim();
            }
            else if (upper.StartsWith("STATIC ") && !upper.StartsWith("STATIC SUB") && !upper.StartsWith("STATIC FUNCTION"))
            {
                dimLine = line.Substring(7).Trim();
            }

            if (dimLine != null && dimLine.Length > 0 && !inType)
            {
                bool isGlobal = isShared || !inProc;
                string dimUpper = dimLine.ToUpper();

                // Pattern A: AS <type> var1, var2, var3 (type-first syntax)
                if (dimUpper.StartsWith("AS "))
                {
                    string afterAs = dimLine.Substring(3).Trim();
                    int spacePos = afterAs.IndexOf(' ');
                    if (spacePos > 0)
                    {
                        string dt = afterAs.Substring(0, spacePos);
                        string varList = afterAs.Substring(spacePos + 1).Trim();
                        foreach (var varPart in SplitTopLevel(varList, ','))
                        {
                            string vName = varPart.Trim();
                            // Strip initializer: "x = 5" -> "x"
                            int eqPos = vName.IndexOf('=');
                            if (eqPos > 0) vName = vName.Substring(0, eqPos).Trim();
                            // Check for array parens in the name itself: "arr(10)" -> "arr"
                            bool hasArrayParens = vName.Contains("(");
                            int parenPos = vName.IndexOf('(');
                            if (parenPos > 0) vName = vName.Substring(0, parenPos).Trim();
                            if (vName.Length > 0 && Regex.IsMatch(vName, @"^\w+$"))
                            {
                                OutlineItemType itemType;
                                if (isRedim) itemType = OutlineItemType.DynArray;
                                else if (hasArrayParens && isGlobal) itemType = OutlineItemType.GlobalArray;
                                else if (hasArrayParens) itemType = OutlineItemType.ArrayDef;
                                else if (isGlobal) itemType = OutlineItemType.GlobalVar;
                                else itemType = OutlineItemType.Variable;
                                items.Add(new OutlineItem
                                {
                                    ItemType = itemType,
                                    Name = vName,
                                    LineNumber = i + 1,
                                    DataType = dt
                                });
                            }
                        }
                    }
                }
                else
                {
                    // Pattern B: var1 AS type [= init], var2 AS type (standard syntax)
                    foreach (var varPart in SplitTopLevel(dimLine, ','))
                    {
                        string part = varPart.Trim();
                        if (part.Length == 0) continue;
                        // Strip initializer first
                        int eqPos = part.IndexOf('=');
                        if (eqPos > 0) part = part.Substring(0, eqPos).Trim();
                        // Extract variable name (first word)
                        var nameMatch = Regex.Match(part, @"^(\w+)");
                        if (nameMatch.Success)
                        {
                            string vName = nameMatch.Groups[1].Value;
                            if (vName.ToUpper() == "AS" || vName.ToUpper() == "SHARED") continue;
                            // Extract type if present
                            string dt = "";
                            var asMatch2 = Regex.Match(part, @"(?i)\bas\s+(\w+)");
                            if (asMatch2.Success) dt = asMatch2.Groups[1].Value;
                            // Check for array parens
                            bool hasArrayParens = part.Contains("(") && part.Contains(")");
                            OutlineItemType itemType;
                            if (isRedim) itemType = OutlineItemType.DynArray;
                            else if (hasArrayParens && isGlobal) itemType = OutlineItemType.GlobalArray;
                            else if (hasArrayParens) itemType = OutlineItemType.ArrayDef;
                            else if (isGlobal) itemType = OutlineItemType.GlobalVar;
                            else itemType = OutlineItemType.Variable;
                            items.Add(new OutlineItem
                            {
                                ItemType = itemType,
                                Name = vName,
                                LineNumber = i + 1,
                                DataType = dt
                            });
                        }
                    }
                }
            }
        }

        return items;
    }
}
