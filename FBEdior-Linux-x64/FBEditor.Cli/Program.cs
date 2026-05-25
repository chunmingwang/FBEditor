using System.Drawing;
using FBEditor.Core;

// Headless proof that the FBEditor engine runs on Linux.
//
// Usage:
//   dotnet run --project FBEditor.Cli                          -> generate single-form demo -> demo.bas
//   dotnet run --project FBEditor.Cli --multi out.bas          -> generate multi-form demo
//   dotnet run --project FBEditor.Cli --save-demo login.w9form -> serialize the demo design to .w9form
//   dotnet run --project FBEditor.Cli --load login.w9form out.bas
//                                                              -> load a .w9form and generate from it
//
// Round-trip test (settles the Color-serialization question — demo button has Back/ForeColor):
//   dotnet run --project FBEditor.Cli --save-demo login.w9form
//   dotnet run --project FBEditor.Cli --load login.w9form out.bas
//   fbc -s gui out.bas && ./out

string? loadPath = GetOption(args, "--load");
string? saveDemo = GetOption(args, "--save-demo");
string? outlinePath = GetOption(args, "--outline");
bool multi = args.Contains("--multi");

// --gdb-check: compile a tiny console program with -g and drive it under gdb,
// hitting a breakpoint in a loop and printing locals. Validates MI parsing on Linux gdb.
if (args.Contains("--gdb-check"))
{
    var gdb = GDBDebugger.FindGDBPath();
    Console.Error.WriteLine($"--- gdb: {(string.IsNullOrEmpty(gdb) ? "NOT FOUND" : gdb)} ---");
    if (string.IsNullOrEmpty(gdb)) return 1;

    AppGlobals.InitializeApp();
    if (string.IsNullOrEmpty(AppGlobals.Build.FBCPath))
        AppGlobals.Build.FBCPath = AppGlobals.FindFBCPath();
    AppGlobals.Build.TargetType = 0;   // console
    AppGlobals.Build.DebugInfo = true; // -g

    var dir = Path.GetTempPath();
    var src = Path.Combine(dir, "fbdbg_test.bas");
    File.WriteAllText(src,
        "dim as integer total = 0\n" +
        "for i as integer = 1 to 3\n" +
        "  total += i          '' breakpoint here (line 3)\n" +
        "next\n" +
        "print \"total=\"; total\n");

    var br = BuildSystem.BuildFile(src);
    if (!br.Success) { Console.Error.WriteLine(br.Output); return 1; }
    var exe = BuildSystem.GetOutputExePath(src);
    Console.Error.WriteLine($"--- compiled with -g: {exe} ---");

    using var dbg = new GDBDebugger();
    var stopped = new System.Threading.ManualResetEventSlim(false);
    int hits = 0;
    dbg.DebugOutput += t => Console.Error.WriteLine("  [gdb] " + t.TrimEnd());
    dbg.DebugError += t => Console.Error.WriteLine("  [ERR] " + t.TrimEnd());
    dbg.LocalsUpdated += locals =>
    {
        foreach (var v in locals)
            Console.Error.WriteLine($"     local: {v.Name} = {v.Value} ({v.DataType})");
    };
    dbg.DebugPaused += (f, l) =>
    {
        hits++;
        Console.Error.WriteLine($"  >> paused at {Path.GetFileName(f)}:{l} (hit #{hits})");
        System.Threading.Thread.Sleep(250); // let the locals/args replies arrive
        dbg.Continue();
    };
    dbg.DebugStopped += () => stopped.Set();

    dbg.AddBreakpoint(src, 3); // the "total += i" line
    if (!dbg.StartDebugging(gdb, exe, src, dir)) { Console.Error.WriteLine("failed to start gdb"); return 1; }
    dbg.Run();

    if (!stopped.Wait(20000)) Console.Error.WriteLine("  (timed out)");
    Console.Error.WriteLine($"--- debugger session complete: {hits} breakpoint hit(s) ---");
    return 0;
}

// --spell: verify the Core spell engine + dictionary loading independent of the UI.
if (args.Contains("--spell"))
{
    var sc = new SpellChecker();
    bool ok = sc.AutoLoad();
    Console.Error.WriteLine($"--- dictionary loaded: {ok}  from: {sc.LoadedFrom ?? "(none found)"} ---");
    Console.Error.WriteLine($"--- engine Enabled: {sc.Enabled} ---");
    foreach (var w in new[] { "coment", "mispeld", "teh", "hello", "integer" })
    {
        var verdict = sc.Check(w) ? "OK" : "MISSPELLED -> " + string.Join(", ", sc.Suggest(w).Take(4));
        Console.Error.WriteLine($"  {w,-10} {verdict}");
    }
    return 0;
}

// --outline: parse a .bas file and print its structure tree (CodeOutline + SyntaxConfig).
if (outlinePath != null)
{
    if (!File.Exists(outlinePath))
    {
        Console.Error.WriteLine($"--- no such file: {outlinePath} ---");
        return 1;
    }
    var src = File.ReadAllText(outlinePath);
    var outline = CodeOutline.ParseOutline(src);
    Console.WriteLine($"Outline of {outlinePath} — {outline.Count} item(s):\n");
    foreach (var group in outline.GroupBy(o => o.Category).OrderBy(g => g.Key))
    {
        Console.WriteLine($"{group.Key}:");
        foreach (var it in group)
        {
            var dt = string.IsNullOrEmpty(it.DataType) ? "" : " : " + it.DataType;
            Console.WriteLine($"  [{it.Icon,-2}] line {it.LineNumber,-4} {it.Name}{dt}");
        }
        Console.WriteLine();
    }
    var ac = SyntaxConfig.GetAutoCompleteList().Split(' ');
    Console.WriteLine($"Auto-complete dictionary: {ac.Length} entries (e.g. {string.Join(", ", ac.Take(6))} ...)");
    return 0;
}

// --save-demo: write the demo design out as .w9form and stop.
if (saveDemo != null)
{
    bool ok = ProjectManager.SaveFormDesign(BuildSingleFormDemo(), saveDemo);
    Console.Error.WriteLine(ok
        ? $"--- wrote demo design to {Path.GetFullPath(saveDemo)} ---"
        : $"--- FAILED to write {saveDemo} ---");
    return ok ? 0 : 1;
}

string outPath = args.Where(a => !a.StartsWith("--") && a != loadPath).LastOrDefault() ?? "demo.bas";
string code;

if (loadPath != null)
{
    var proj = ProjectManager.LoadFormProject(loadPath);
    if (proj == null)
    {
        Console.Error.WriteLine($"--- could not load {loadPath} ---");
        return 1;
    }
    Console.Error.WriteLine($"--- loaded {proj.Forms.Count} form(s) from {loadPath} ---");
    code = W9CodeGenerator.GenerateMultiFormCode(proj);
}
else if (multi)
{
    code = BuildMultiFormDemo();
}
else
{
    code = W9CodeGenerator.GenerateCode(BuildSingleFormDemo());
}

Console.WriteLine(code);
File.WriteAllText(outPath, code);
Console.Error.WriteLine($"\n--- wrote {code.Length} chars to {Path.GetFullPath(outPath)} ---");

// --build: compile the generated .bas through BuildSystem (the engine's own compiler driver).
// --run: also launch the result afterwards.
if (args.Contains("--build"))
{
    AppGlobals.InitializeApp();
    if (string.IsNullOrEmpty(AppGlobals.Build.FBCPath))
        AppGlobals.Build.FBCPath = AppGlobals.FindFBCPath();
    AppGlobals.Build.TargetType = 1; // -s gui (Window9 needs the GUI subsystem)

    Console.Error.WriteLine($"--- fbc: {(string.IsNullOrEmpty(AppGlobals.Build.FBCPath) ? "NOT FOUND" : AppGlobals.Build.FBCPath)} ---");
    var br = BuildSystem.BuildFile(Path.GetFullPath(outPath), runAfter: args.Contains("--run"));
    Console.Error.WriteLine(br.Output);

    // Demonstrate the clickable-error parser on whatever fbc returned.
    var parsed = AppGlobals.ParseCompilerErrors(br.Output, Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? "");
    if (parsed.Count > 0)
    {
        Console.Error.WriteLine($"--- parsed {parsed.Count} diagnostic(s): ---");
        foreach (var e in parsed) Console.Error.WriteLine("    " + e);
    }
    return br.Success ? 0 : 1;
}

Console.Error.WriteLine($"--- compile on Devuan with:  fbc -s gui {outPath}");
return 0;

// ---------------------------------------------------------------------------

static string? GetOption(string[] a, string name)
{
    int i = Array.IndexOf(a, name);
    return (i >= 0 && i + 1 < a.Length) ? a[i + 1] : null;
}

static W9FormDesign BuildSingleFormDemo()
{
    var design = new W9FormDesign
    {
        FormTitle = "Kol HaAm Login",
        FormWidth = 420,
        FormHeight = 320,
        BaseWidth = 420,
        BaseHeight = 320,
        HasKeyboardShortcut = true
    };

    int id = design.GadgetEnumStart + 1;

    design.Gadgets.Add(new W9GadgetInstance
    {
        ID = id++, GadgetType = W9GadgetType.TextLabel, EnumName = "giLabel1",
        Text = "Username:", X = 20, Y = 20, W = 100, H = 22
    });
    design.Gadgets.Add(new W9GadgetInstance
    {
        ID = id++, GadgetType = W9GadgetType.StringInput, EnumName = "giString1",
        X = 130, Y = 18, W = 250, H = 24, OnChangeEvent = true
    });
    design.Gadgets.Add(new W9GadgetInstance
    {
        ID = id++, GadgetType = W9GadgetType.TextLabel, EnumName = "giLabel2",
        Text = "Password:", X = 20, Y = 56, W = 100, H = 22
    });
    design.Gadgets.Add(new W9GadgetInstance
    {
        ID = id++, GadgetType = W9GadgetType.StringInput, EnumName = "giString2",
        X = 130, Y = 54, W = 250, H = 24, IsPassword = true
    });
    design.Gadgets.Add(new W9GadgetInstance
    {
        ID = id++, GadgetType = W9GadgetType.CheckBox, EnumName = "giCheckBox1",
        Text = "Remember me", X = 130, Y = 90, W = 160, H = 24, IsChecked = true
    });
    design.Gadgets.Add(new W9GadgetInstance
    {
        ID = id++, GadgetType = W9GadgetType.ListBox, EnumName = "giListBox1",
        X = 20, Y = 124, W = 360, H = 110, Items = "Hebrew\nEnglish\nArabic\nPersian",
        OnDoubleClickEvent = true
    });
    var loginBtn = new W9GadgetInstance
    {
        ID = id++, GadgetType = W9GadgetType.Button, EnumName = "giButton1",
        Text = "Log In", X = 280, Y = 244, W = 100, H = 30,
        BackColor = Color.FromArgb(40, 90, 160), ForeColor = Color.White
    };
    design.Gadgets.Add(loginBtn);
    design.DefaultButtonID = loginBtn.ID; // Enter key triggers login

    // A small menu
    var fileMenu = new W9MenuItemInfo { EnumName = "miFile", Text = "File", IsTopLevel = true };
    fileMenu.Children.Add(new W9MenuItemInfo { EnumName = "miExit", Text = "Exit" });
    design.MenuItems.Add(fileMenu);

    return design;
}

static string BuildMultiFormDemo()
{
    var project = new W9FormProject();      // ctor creates the main form
    var main = project.MainForm!;
    main.FormTitle = "Main Window";
    main.Gadgets.Add(new W9GadgetInstance
    {
        ID = 101, GadgetType = W9GadgetType.Button, EnumName = "giButton1",
        Text = "About...", X = 20, Y = 20, W = 120, H = 30
    });

    var about = project.AddChildForm("About");
    about.Gadgets.Add(new W9GadgetInstance
    {
        ID = 201, GadgetType = W9GadgetType.TextLabel, EnumName = "giLabel1",
        Text = "FBEditor — Avalonia port", X = 20, Y = 20, W = 300, H = 22
    });

    return W9CodeGenerator.GenerateMultiFormCode(project);
}
