namespace Restage.Services.Api;

/// <summary>
/// Turns what was said into a command: "open portfolio", "ga naar
/// portfolio alsjeblieft", "save this as Budget". English and Dutch. The
/// name keeps its casing (a capture is named with it); matching it against
/// the workspaces is <see cref="NameMatcher"/>'s job.
/// </summary>
public static class CommandParser
{
    public enum Kind
    {
        /// <summary>Start the named workspace (also when no verb was said).</summary>
        Restore,
        /// <summary>Capture the desktop as a workspace with the name.</summary>
        Capture,
    }

    public sealed record Command(Kind Kind, string Name);

    // Longest phrases first, so "save this as" wins over "save".
    static readonly string[][] Address = Phrases("hey restage", "he restage", "restage", "hey", "please", "alsjeblieft");
    static readonly string[][] CaptureVerbs = Phrases(
        "save this as", "capture this as", "remember this as", "save as", "capture as", "remember as", "save", "capture", "remember",
        "sla dit op als", "sla op als", "bewaar dit als", "onthoud dit als", "bewaar als", "onthoud als", "sla op", "bewaar", "onthoud");
    static readonly string[][] RestoreVerbs = Phrases(
        "switch over to", "schakel over naar", "switch to", "go to", "open up", "bring up", "ga naar", "schakel naar", "wissel naar",
        "open", "start", "restore", "resume", "load", "launch", "show", "switch", "herstel", "laad", "toon", "wissel", "activeer");
    static readonly string[][] Articles = Phrases("the", "my", "a", "de", "het", "mijn", "een", "workspace", "werkruimte");
    static readonly HashSet<string> Trailing = new(StringComparer.Ordinal)
        { "workspace", "werkruimte", "please", "alsjeblieft", "alstublieft", "now", "nu", "graag" };

    /// <summary>The command in the text, or null when nothing was said.</summary>
    public static Command? Parse(string text)
    {
        var words = Tokens(text);
        if (words.Count == 0) return null;
        var lower = words.Select(w => w.ToLowerInvariant()).ToList();

        var at = 0;
        while (Skip(lower, ref at, Address)) { }
        var kind = Kind.Restore;
        if (Skip(lower, ref at, CaptureVerbs)) kind = Kind.Capture;
        else Skip(lower, ref at, RestoreVerbs);
        while (Skip(lower, ref at, Articles)) { }
        var end = words.Count;
        while (end > at && Trailing.Contains(lower[end - 1])) end--;

        // "Open" alone, or a workspace that is called like a verb: the whole text is the name.
        if (end <= at && kind == Kind.Restore) return new Command(kind, string.Join(' ', words));
        return new Command(kind, string.Join(' ', words.Skip(at).Take(end - at)));
    }

    static bool Skip(List<string> lower, ref int at, string[][] phrases)
    {
        foreach (var phrase in phrases)
        {
            if (at + phrase.Length > lower.Count) continue;
            var hit = true;
            for (var i = 0; i < phrase.Length && hit; i++) hit = lower[at + i] == phrase[i];
            if (!hit) continue;
            at += phrase.Length;
            return true;
        }
        return false;
    }

    static string[][] Phrases(params string[] phrases) =>
        phrases.Select(p => p.Split(' ')).OrderByDescending(p => p.Length).ToArray();

    /// <summary>Words with punctuation trimmed off their ends; "Portfolio," → "Portfolio".</summary>
    static List<string> Tokens(string text)
    {
        var list = new List<string>();
        foreach (var raw in text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var start = 0;
            var end = raw.Length;
            while (start < end && !char.IsLetterOrDigit(raw[start])) start++;
            while (end > start && !char.IsLetterOrDigit(raw[end - 1])) end--;
            if (end > start) list.Add(raw[start..end]);
        }
        return list;
    }
}
