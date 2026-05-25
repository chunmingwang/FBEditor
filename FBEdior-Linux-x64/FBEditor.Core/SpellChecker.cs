using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WeCantSpell.Hunspell;

namespace FBEditor.Core;

/// <summary>
/// Spell-checking via WeCantSpell.Hunspell (the managed replacement for the VB
/// version's NHunspell). Loads a Hunspell .dic/.aff pair; AutoLoad probes the
/// common locations including the system dictionaries Debian/Devuan install via
/// the 'hunspell-en-us' / myspell packages.
/// </summary>
public sealed class SpellChecker
{
    private WordList? _words;

    public bool Enabled => _words != null;
    public string? LoadedFrom { get; private set; }

    public bool TryLoad(string dicPath, string affPath)
    {
        try
        {
            if (!File.Exists(dicPath) || !File.Exists(affPath)) return false;
            _words = WordList.CreateFromFiles(dicPath, affPath);
            LoadedFrom = dicPath;
            return true;
        }
        catch
        {
            _words = null;
            return false;
        }
    }

    /// <summary>Try the common dictionary locations; returns true if one loaded.</summary>
    public bool AutoLoad()
    {
        foreach (var (dic, aff) in CandidatePaths())
            if (TryLoad(dic, aff)) return true;
        return false;
    }

    private static IEnumerable<(string dic, string aff)> CandidatePaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cfg = Path.Combine(home, ".config", "FBEditor", "dict");
        yield return (Path.Combine(cfg, "en_US.dic"), Path.Combine(cfg, "en_US.aff"));

        // System dictionaries (apt install hunspell-en-us / myspell-en-us)
        yield return ("/usr/share/hunspell/en_US.dic", "/usr/share/hunspell/en_US.aff");
        yield return ("/usr/share/myspell/en_US.dic", "/usr/share/myspell/en_US.aff");
        yield return ("/usr/share/myspell/dicts/en_US.dic", "/usr/share/myspell/dicts/en_US.aff");

        var appDir = AppGlobals.AppPath;
        if (!string.IsNullOrEmpty(appDir))
            yield return (Path.Combine(appDir, "en_US.dic"), Path.Combine(appDir, "en_US.aff"));
    }

    /// <summary>True if the word is spelled correctly (or no dictionary is loaded).</summary>
    public bool Check(string word) => _words == null || _words.Check(word);

    public IEnumerable<string> Suggest(string word) =>
        _words == null ? Enumerable.Empty<string>() : _words.Suggest(word);
}
