using System.Text.Json;
using System.Text.RegularExpressions;
using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Chrome, Edge, Brave and friends. All windows of a profile live in one
/// process whose command line only tells how the first one was started, so the
/// adapter reads each window's AppUserModelID instead: installed web apps carry
/// "…_crx_&lt;app id&gt;", browser windows do not. Browser windows are restored by
/// starting the profile once (the browser restores its tabs); each web app is
/// started with --app-id.
/// </summary>
public sealed class ChromiumAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.Chromium;

    static readonly Dictionary<string, string> UserDataDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = @"Google\Chrome\User Data",
        ["msedge"] = @"Microsoft\Edge\User Data",
        ["brave"] = @"BraveSoftware\Brave-Browser\User Data",
        ["vivaldi"] = @"Vivaldi\User Data",
        ["chromium"] = @"Chromium\User Data",
    };

    static readonly Regex AppIdPattern = new(@"_crx_(?<id>[a-p]{32})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override bool CanDescribe(CapturedWindow window) =>
        UserDataDirs.ContainsKey(window.ProcessName) && !window.Args.Contains("--type=");

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var profiles = LoadProfiles(window.ProcessName);
        var profile = ProfileDirectory(window, profiles.Keys);
        var profileLabel = profiles.TryGetValue(profile, out var n) && n.Length > 0 ? n : profile;
        var appId = AppIdFromAumid(window.AppUserModelId);
        if (appId != null)
        {
            return new WorkspaceItem
            {
                Kind = ItemKind.ChromiumApp,
                Name = $"{window.ProcessName} app",
                Title = window.Title,
                Exe = window.ExePath,
                Args = $"--profile-directory={LaunchHelpers.Quote(profile)} --app-id={appId}",
                Target = $"{profile}|{appId}",
                Detail = $"{window.Title} · {profileLabel}",
            };
        }
        return new WorkspaceItem
        {
            Kind = ItemKind.Chromium,
            Name = window.ProcessName,
            Title = window.Title,
            Exe = window.ExePath,
            Args = $"--profile-directory={LaunchHelpers.Quote(profile)}",
            Target = profile,
            Detail = $"Profile: {profileLabel}",
        };
    }

    public override int Match(WorkspaceItem item, CapturedWindow window)
    {
        if (!CanDescribe(window) || !Same(item.Exe, window.ExePath)) return 0;
        var liveApp = AppIdFromAumid(window.AppUserModelId);
        if (item.Kind == ItemKind.ChromiumApp)
        {
            var wantedApp = item.Target?.Split('|').LastOrDefault();
            return liveApp != null && Same(liveApp, wantedApp) ? 3 : 0;
        }
        if (liveApp != null) return 0;
        // Browser windows: the AUMID names the profile for non-default ones;
        // the command line is the fallback for the default profile.
        var liveProfile = ProfileFromAumid(window.AppUserModelId, item.Target) ?? ProfileFromArgs(window.Args) ?? "Default";
        if (!Same(liveProfile, item.Target ?? "Default")) return 0;
        return Same(item.Title, window.Title) ? 3 : 2;
    }

    /// <summary>One launch per (browser, profile) for browser windows; web apps launch singly.</summary>
    public override string? LaunchGroup(WorkspaceItem item) =>
        item.Kind == ItemKind.Chromium ? $"{item.Exe}|{item.Target}" : null;

    public override Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct)
    {
        var item = group[0];
        if (!File.Exists(item.Exe)) return Task.FromResult(LaunchResult.Failed($"not found: {item.Exe}"));
        try
        {
            LaunchHelpers.Start(item.Exe, item.Args);
            return Task.FromResult(new LaunchResult(true, $"started {item.Detail}")
            {
                WindowWait = LaunchHelpers.HeavyWait,
                Gap = LaunchHelpers.HeavyGap,
            });
        }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Failed(ex.Message)); }
    }

    /// <summary>"Chrome._crx_abc…" / "Chrome.Profile1._crx_abc…" → the 32-letter app id; null for browser windows.</summary>
    public static string? AppIdFromAumid(string? aumid) =>
        aumid != null && AppIdPattern.Match(aumid) is { Success: true } m ? m.Groups["id"].Value.ToLowerInvariant() : null;

    /// <summary>
    /// The profile segment of an AUMID ("Chrome.Profile1" → "Profile1"), mapped
    /// back to a real profile directory when one matches after removing spaces.
    /// Null when the AUMID has no profile segment (default profile).
    /// </summary>
    public static string? ProfileFromAumid(string? aumid, string? candidate = null)
    {
        if (string.IsNullOrEmpty(aumid)) return null;
        // Chrome joins "Chrome", the app name ("_crx_…", web apps only) and the
        // profile directory without spaces with dots, leaving the default profile
        // out. Drop the app part; the profile is then whatever follows "Chrome".
        var parts = AppIdPattern.Replace(aumid, "").Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var segment = parts[1];
        if (candidate != null && Same(segment, candidate.Replace(" ", ""))) return candidate;
        return segment;
    }

    public static string? ProfileFromArgs(string args)
    {
        var tokens = LaunchHelpers.Tokenize(args);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].StartsWith("--profile-directory=", StringComparison.OrdinalIgnoreCase))
                return tokens[i]["--profile-directory=".Length..];
            if (tokens[i].Equals("--profile-directory", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                return tokens[i + 1];
        }
        return null;
    }

    static string ProfileDirectory(CapturedWindow window, IEnumerable<string> knownProfiles)
    {
        var fromAumid = ProfileFromAumid(window.AppUserModelId);
        if (fromAumid != null)
        {
            var real = knownProfiles.FirstOrDefault(p => Same(p.Replace(" ", ""), fromAumid));
            return real ?? fromAumid;
        }
        return ProfileFromArgs(window.Args) ?? "Default";
    }

    /// <summary>Profile directory → display name, from the browser's Local State file.</summary>
    public static Dictionary<string, string> LoadProfiles(string processName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!UserDataDirs.TryGetValue(processName, out var rel)) return result;
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), rel, "Local State");
        try
        {
            if (!File.Exists(file)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("profile", out var profile) ||
                !profile.TryGetProperty("info_cache", out var cache)) return result;
            foreach (var p in cache.EnumerateObject())
                result[p.Name] = p.Value.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        }
        catch { }
        return result;
    }
}
