using System.Globalization;
using System.Text;

namespace Restage.Services.Api;

/// <summary>
/// Finds the workspace a phrase names. Spoken text comes back with casing,
/// punctuation and extra words, and a name is sometimes heard slightly wrong,
/// so the comparison is on normalised text with some tolerance, and only a
/// clear winner counts.
/// </summary>
public static class NameMatcher
{
    public sealed record Candidate(string Id, string Name, int Score);

    /// <summary>A candidate counts from this score; below it the phrase names nothing.</summary>
    public const int MinScore = 60;

    /// <summary>Two candidates closer than this are a tie: nothing is started.</summary>
    public const int MinLead = 8;

    /// <summary>The one workspace the phrase names, or null when none does or two could.</summary>
    public static Candidate? Best(string phrase, IEnumerable<(string Id, string Name)> workspaces)
    {
        var ranked = Rank(phrase, workspaces);
        if (ranked.Count == 0 || ranked[0].Score < MinScore) return null;
        if (ranked.Count > 1 && ranked[1].Score >= ranked[0].Score - MinLead) return null;
        return ranked[0];
    }

    /// <summary>Every workspace that resembles the phrase at all, best first.</summary>
    public static IReadOnlyList<Candidate> Rank(string phrase, IEnumerable<(string Id, string Name)> workspaces)
    {
        var spoken = Normalize(phrase);
        if (spoken.Length == 0) return [];
        var list = new List<Candidate>();
        foreach (var (id, name) in workspaces)
        {
            var target = Normalize(name);
            if (target.Length == 0) continue;
            var score = Score(spoken, target);
            if (score > 0) list.Add(new Candidate(id, name, score));
        }
        return list.OrderByDescending(c => c.Score).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static int Score(string spoken, string name)
    {
        if (spoken == name) return 100;
        if (ContainsPhrase(spoken, name)) return 90;                                                   // "portfolio now"
        if (spoken.Length >= 3 && name.StartsWith(spoken + " ", StringComparison.Ordinal)) return 80;  // "design" for "Design notes"
        if (spoken.Length >= 3 && ContainsPhrase(name, spoken)) return 70;                             // a later word of the name
        // Misheard or split differently: the whole phrase, and the name against
        // the same number of spoken words, so words around it cost nothing.
        var similarity = Math.Max(Similarity(spoken, name), BestWindow(spoken, name));
        return similarity >= 0.6 ? (int)Math.Round(similarity * 85) : 0;
    }

    static bool ContainsPhrase(string text, string phrase) =>
        (" " + text + " ").Contains(" " + phrase + " ", StringComparison.Ordinal);

    static double BestWindow(string spoken, string name)
    {
        var words = spoken.Split(' ');
        var size = name.Split(' ').Length;
        if (words.Length <= size) return 0;
        var best = 0.0;
        for (var i = 0; i + size <= words.Length; i++)
            best = Math.Max(best, Similarity(string.Join(' ', words, i, size), name));
        return best;
    }

    /// <summary>1 for equal strings, 0 for nothing in common: one minus the edit distance over the longer length.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        return 1.0 - (double)EditDistance(a, b) / Math.Max(a.Length, b.Length);
    }

    static int EditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>Lower case, no accents, letters and digits only, single spaces.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = true;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); space = false; }
            else if (!space) { sb.Append(' '); space = true; }
        }
        return sb.ToString().Trim();
    }
}
