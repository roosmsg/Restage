using System.Globalization;

namespace Restage.Services;

/// <summary>Short English phrases for "when": "just now", "5 minutes ago", "yesterday", "7 Nov".</summary>
public static class RelativeTime
{
    /// <summary>How long ago <paramref name="then"/> was, seen from <paramref name="now"/>; a date once it is a week back.</summary>
    public static string Ago(DateTime then, DateTime now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return Plural((int)span.TotalMinutes, "minute") + " ago";
        if (span < TimeSpan.FromHours(24)) return Plural((int)span.TotalHours, "hour") + " ago";
        var days = (now.Date - then.Date).Days;
        if (days <= 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        return Date(then, now);
    }

    /// <summary>"today", "yesterday", else the date: "7 Nov", with the year once it differs from now's.</summary>
    public static string Date(DateTime then, DateTime now)
    {
        var days = (now.Date - then.Date).Days;
        if (days == 0) return "today";
        if (days == 1) return "yesterday";
        return then.ToString(then.Year == now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.InvariantCulture);
    }

    static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")}";
}
