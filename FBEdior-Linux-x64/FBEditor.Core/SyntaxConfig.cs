namespace FBEditor.Core;

/// <summary>
/// FreeBASIC keyword / type / function / preprocessor word lists and the
/// combined auto-complete list. Ported from Modules/SyntaxConfig.vb.
/// </summary>
public static class SyntaxConfig
{
    // FreeBASIC Keywords (language keywords)
    public const string FB_KEYWORDS =
        "abs access alias and andalso any append as asm assert " +
        "base beep bin byref byval " +
        "call callocate case cast cbyte cdbl cint clng clngint " +
        "close cls color common cons const constructor continue " +
        "cptr cshort csign csng cubyte cuint culng culngint cunsg cushort " +
        "data date deallocate declare defbyte defdbl defined defint deflng " +
        "deflngint defshort defsng defstr defubyte defuint defulng " +
        "defulngint defushort delete destructor dim dir do draw dynamic " +
        "else elseif encoding end endif enum erase err error event " +
        "exec exepath exit explicit export extends extern " +
        "false field fix flip for fre freefile function " +
        "get getjoystick getkey getmouse gosub goto " +
        "hex hibyte hiword " +
        "if iif imagecreate imagedestroy imageinfo imp implements " +
        "import in include inkey inp input instr int is " +
        "kill " +
        "lbound lcase left len let lib line lobyte loc local locate " +
        "lock lof log long loop loword lpos lprint lset ltrim " +
        "mid mkd mki mkl mklongint mks mkshort mkdir mod multikey " +
        "mutexcreate mutexdestroy mutexlock mutexunlock " +
        "naked namespace new next not " +
        "object oct on once open operator option or orelse out output " +
        "overload override " +
        "paint palette pcopy peek pipe pmap point poke pos " +
        "preserve preset print private procptr property protected " +
        "pset public put " +
        "random randomize read reallocate redim rem reset restore " +
        "resume return right rmdir rnd rset rtrim run " +
        "sadd scope screen screencontrol screencopy screenevent " +
        "screeninfo screenlist screenlock screenptr screenres " +
        "screenset screenunlock seek select setdate setenviron " +
        "setmouse settime sgn shared shell shl shr sin sizeof " +
        "sleep space spc sqr static stdcall step stop str " +
        "string sub swap " +
        "tab tan then this threadcall threadcreate threaddetach " +
        "threadwait time timer to trans trim true type typeof " +
        "ubound ucase union unlock until using " +
        "va_arg va_first va_next val valint vallng valulng valuint " +
        "var varptr view virtual " +
        "wait wbin wchr wend while width window windowtitle " +
        "winput with woct write wspace wstr " +
        "xor year";

    // FreeBASIC Data Types
    public const string FB_TYPES =
        "boolean byte double integer long longint " +
        "object pointer ptr short single string ubyte " +
        "uinteger ulong ulongint ushort unsigned " +
        "wstring zstring any";

    // FreeBASIC Preprocessor
    public const string FB_PREPROCESSOR =
        "#assert #define #else #elseif #endif #endmacro #error #if #ifdef " +
        "#ifndef #inclib #include #lang #libpath #line #macro #pragma #print #undef";

    // FreeBASIC Built-in Functions
    public const string FB_FUNCTIONS =
        "abs acos allocate asin atan2 atn bin callocate chr clear command " +
        "condbroadcast condcreate conddestroy condsignal condwait cos csrlin " +
        "cvd cvi cvl cvlongint cvs cvshort date dir dylibfree dylibload " +
        "dylibsymbol environ eof err error exp expath fileattr filecopy " +
        "filedatetime fileexists filelen fix format frac fre freefile hex " +
        "hibyte hiword hour imageconvertrow imagecreate imagedestroy imageinfo " +
        "inkey inp instr instrrev int isdate kill lbound lcase left len " +
        "loc lof log lobyte loword lset ltrim mid minute mkd mki mkl " +
        "mklongint mks mkshort month monthname multikey mutexcreate " +
        "mutexdestroy mutexlock mutexunlock name now oct offsetof pcopy " +
        "peek pmap point pointcoord pos rset randomize rgb rgba right rnd " +
        "rtrim sadd screen screencontrol screencopy screenevent screenglproc " +
        "screeninfo screenlist screenlock screenptr screenres screenset " +
        "screensync screenunlock second seek setdate setenviron setmouse " +
        "settime sgn sin sizeof space spc sqr str strptr swap tab tan " +
        "threadcreate threaddetach threadself threadwait time timeserial " +
        "timevalue timer trim ucase ubound val vallng valint valuint valulng " +
        "varptr weekday weekdayname write year";

    /// <summary>Auto-complete combined list (sorted, space-separated).</summary>
    public static string GetAutoCompleteList()
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in FB_KEYWORDS.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            all.Add(w.ToUpper());
        foreach (var w in FB_TYPES.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            all.Add(w.ToUpper());
        foreach (var w in FB_FUNCTIONS.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            all.Add(w.ToUpper());
        foreach (var w in FB_PREPROCESSOR.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            all.Add(w);
        var sorted = all.ToList();
        sorted.Sort();
        return string.Join(" ", sorted);
    }

    private static List<string>? _autoComplete;
    /// <summary>Cached, sorted list of completion words (keywords, types, functions, preprocessor).</summary>
    public static IReadOnlyList<string> AutoCompleteWords =>
        _autoComplete ??= GetAutoCompleteList().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
}
