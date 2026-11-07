using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Restage.Services;

/// <summary>Process-starting helpers shared by the adapters.</summary>
public static class LaunchHelpers
{
    // Apps whose startup spikes CPU/disk hard enough to deserve a longer wait
    // for their window and a longer gap before the next launch.
    public static readonly HashSet<string> HeavyApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "firefox", "opera", "vivaldi",
        "devenv", "code", "code - insiders", "vscodium", "rider64", "idea64", "studio64", "obsidian",
        "teams", "ms-teams", "msteams", "excel", "winword", "powerpnt", "photoshop", "blender",
    };

    public static readonly TimeSpan LightWait = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan HeavyWait = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan LightGap = TimeSpan.FromMilliseconds(700);
    public static readonly TimeSpan HeavyGap = TimeSpan.FromMilliseconds(2000);

    // Flags that would make a relaunch start hidden or as a helper process.
    static readonly string[] DroppedFlags =
    {
        "--start-in-tray", "--start-minimized", "--minimized", "-minimized", "--hidden", "--background",
        "--silent-start", "--no-startup-window", "--startup", "/minimized",
    };

    public static bool IsHeavy(string processName) => HeavyApps.Contains(processName);

    /// <summary>
    /// Packaged (Store) apps cannot be started by their exe path under
    /// WindowsApps. Most register an App Execution Alias; this returns it (or
    /// null when there is none and the exe is not a Store app).
    /// </summary>
    public static string? StoreAliasFor(string exePath)
    {
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        if (!exePath.StartsWith(windowsApps, StringComparison.OrdinalIgnoreCase)) return null;
        var name = Path.GetFileName(exePath);
        if (name.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)) return "wt.exe";
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps", name);
        return File.Exists(alias) ? alias : "";
    }

    /// <summary>
    /// Captured command lines carry flags that break an interactive relaunch:
    /// COM activation (-Embedding), Chromium child processes (--type=), start-
    /// hidden switches. Keep only what helps.
    /// </summary>
    public static string SanitizeArgs(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return "";
        if (args.Contains("-Embedding", StringComparison.OrdinalIgnoreCase) ||
            args.Contains("/Embedding", StringComparison.OrdinalIgnoreCase) ||
            args.Contains("--type=", StringComparison.OrdinalIgnoreCase))
            return "";
        var kept = Tokenize(args).Where(t => !DroppedFlags.Contains(t, StringComparer.OrdinalIgnoreCase));
        return Join(kept);
    }

    /// <summary>Splits a command line into tokens, honouring double quotes.</summary>
    public static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) { tokens.Add(sb.ToString()); sb.Clear(); any = false; }
                continue;
            }
            sb.Append(c);
            any = true;
        }
        if (any) tokens.Add(sb.ToString());
        return tokens;
    }

    /// <summary>Quotes tokens that need it and joins them with spaces.</summary>
    public static string Join(IEnumerable<string> tokens) =>
        string.Join(" ", tokens.Select(Quote));

    public static string Quote(string token) =>
        token.Length == 0 || token.Any(char.IsWhiteSpace) ? $"\"{token}\"" : token;

    /// <summary>Starts a program (or URI) through the shell; returns the process when one was created.</summary>
    public static Process? Start(string fileName, string args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo { FileName = fileName, Arguments = args, UseShellExecute = true };
        if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        return Process.Start(psi);
    }

    /// <summary>Case-insensitive "contains" for arguments.</summary>
    public static bool HasFlag(string args, string flag) =>
        Regex.IsMatch(args, $@"(^|\s){Regex.Escape(flag)}(\s|=|$)", RegexOptions.IgnoreCase);
}
