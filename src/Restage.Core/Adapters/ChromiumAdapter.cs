using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Chrome, Edge, Brave and friends. All windows of a profile live in one
/// process whose command line only tells how the first one was started, so the
/// adapter reads each window's AppUserModelID instead: installed web apps carry
/// "_crx_&lt;app id&gt;", browser windows do not. Browser windows are restored by
/// starting the profile once (the browser restores its tabs); each web app is
/// started with --app-id.
///
/// Chrome builds the AppUserModelID from its base id ("Chrome"), the app name
/// ("_crx_" + the 32-letter app id, web apps only) and the profile directory
/// (without spaces and dots; left out for "Default"), joined with dots and
/// limited to 64 characters: a component over its share keeps its head and
/// tail. A web app in the default profile therefore shows 26 of its 32
/// letters; the full id is found by rebuilding the AppUserModelID of every web
/// app installed in the profile and picking the one that matches.
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

    /// <summary>Chrome's own limit for an AppUserModelID (ShellUtil::kMaxAppModelIdLength).</summary>
    const int MaxAppModelIdLength = 64;

    /// <summary>The app component of an AppUserModelID; the id is often shortened, see <see cref="BuildAumid"/>.</summary>
    static readonly Regex AppIdPattern = new(@"^_crx_(?<id>[a-p]{1,32})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override bool CanDescribe(CapturedWindow window) =>
        UserDataDirs.ContainsKey(window.ProcessName) && !window.Args.Contains("--type=");

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var profiles = LoadProfiles(window.ProcessName);
        var profile = ProfileDirectory(window, profiles.Keys);
        var profileLabel = profiles.TryGetValue(profile, out var n) && n.Length > 0 ? n : profile;
        var isDefault = Same(profile, "Default");
        var appId = ResolveAppId(window.AppUserModelId, profile, InstalledAppIds(window.ProcessName, profile));
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
                Detail = isDefault ? window.Title : $"{window.Title} · {profileLabel}",
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
            var parts = (item.Target ?? "").Split('|');
            if (liveApp == null || parts.Length != 2) return 0;
            // The live id may be shortened: rebuild what Chrome gives this app in this profile.
            if (Same(liveApp, parts[1])) return 3;
            return Same(BuildAumid(BaseId(window.AppUserModelId), parts[1], parts[0]), window.AppUserModelId) ? 3 : 0;
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

    /// <summary>
    /// "Chrome._crx_abc…" → the letters after "_crx_" (26 of the 32 when Chrome
    /// shortened the component); null for browser windows.
    /// </summary>
    public static string? AppIdFromAumid(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return null;
        foreach (var part in aumid.Split('.'))
            if (AppIdPattern.Match(part) is { Success: true } m) return m.Groups["id"].Value.ToLowerInvariant();
        return null;
    }

    /// <summary>
    /// The full 32-letter app id behind a window's AppUserModelID: the id itself
    /// when it is complete, else the installed app whose rebuilt AppUserModelID
    /// equals the window's. Falls back to the shortened id so the window is at
    /// least shown as a web app.
    /// </summary>
    public static string? ResolveAppId(string? aumid, string profile, IEnumerable<string> installedAppIds)
    {
        var shortId = AppIdFromAumid(aumid);
        if (shortId == null || shortId.Length == 32) return shortId;
        var baseId = BaseId(aumid);
        foreach (var id in installedAppIds)
            if (Same(BuildAumid(baseId, id, profile), aumid)) return id;
        return shortId;
    }

    /// <summary>
    /// The AppUserModelID Chrome gives a window of <paramref name="appId"/> (null
    /// for a browser window) in <paramref name="profile"/>: the components joined
    /// with dots, each cut to its share of 64 characters by keeping head and tail.
    /// </summary>
    public static string BuildAumid(string baseId, string? appId, string? profile)
    {
        var components = new List<string> { baseId };
        if (appId != null) components.Add("_crx_" + appId);
        var profileId = ProfileIdForAumid(profile);
        if (profileId.Length > 0) components.Add(profileId);
        var sb = new StringBuilder();
        foreach (var c in components)
        {
            if (sb.Length > 0) sb.Append('.');
            sb.Append(ShortenComponent(c, components.Count));
        }
        return sb.ToString();
    }

    /// <summary>Cuts a component over its share of the 64 characters in the middle, as Chrome does.</summary>
    static string ShortenComponent(string component, int componentCount)
    {
        var max = (MaxAppModelIdLength - (componentCount - 1)) / componentCount;
        return component.Length <= max ? component : component[..(max / 2)] + component[^((max + 1) / 2)..];
    }

    /// <summary>The profile component of an AppUserModelID: empty for Default, else the directory name without spaces and dots.</summary>
    static string ProfileIdForAumid(string? profile) =>
        profile == null || Same(profile, "Default") ? "" : profile.Replace(" ", "").Replace(".", "");

    static string BaseId(string? aumid) => aumid is { Length: > 0 } ? aumid.Split('.')[0] : "Chrome";

    /// <summary>
    /// The profile component of an AUMID ("Chrome.Profile1" → "Profile1",
    /// "Chrome._crx_….Profile1" → "Profile1"), returned as the real directory
    /// name when <paramref name="candidate"/> produces that component. Null when
    /// the AUMID has no profile component (default profile).
    /// </summary>
    public static string? ProfileFromAumid(string? aumid, string? candidate = null)
    {
        if (string.IsNullOrEmpty(aumid)) return null;
        var raw = aumid.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var parts = raw.Where(p => !AppIdPattern.IsMatch(p)).ToList();
        if (parts.Count < 2) return null;
        var segment = parts[1];
        if (candidate != null && Same(segment, ShortenComponent(ProfileIdForAumid(candidate), raw.Length))) return candidate;
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

    /// <summary>
    /// The profile directory a window belongs to. Only directories the browser
    /// itself lists are returned: an AUMID component that matches none of them
    /// must not become a --profile-directory, or the browser would create it.
    /// </summary>
    static string ProfileDirectory(CapturedWindow window, IEnumerable<string> knownProfiles)
    {
        if (ProfileFromAumid(window.AppUserModelId) != null)
        {
            var real = knownProfiles.FirstOrDefault(p => ProfileFromAumid(window.AppUserModelId, p) == p);
            if (real != null) return real;
        }
        return ProfileFromArgs(window.Args) ?? "Default";
    }

    /// <summary>Profile directory → display name, from the browser's Local State file.</summary>
    public static Dictionary<string, string> LoadProfiles(string processName)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!UserDataDirs.TryGetValue(processName, out var rel)) return result;
        var file = Path.Combine(LocalAppData, rel, "Local State");
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

    /// <summary>
    /// Ids of the web apps installed in a profile, read from the directory names
    /// the browser keeps their icons and manifest resources in.
    /// </summary>
    public static IReadOnlyList<string> InstalledAppIds(string processName, string profile)
    {
        var ids = new List<string>();
        if (!UserDataDirs.TryGetValue(processName, out var rel)) return ids;
        var root = Path.Combine(LocalAppData, rel, profile, "Web Applications");
        try
        {
            if (!Directory.Exists(root)) return ids;
            foreach (var dir in Directory.EnumerateDirectories(root, "_crx_*"))
                ids.Add(Path.GetFileName(dir)[5..].ToLowerInvariant());
            var manifests = Path.Combine(root, "Manifest Resources");
            if (Directory.Exists(manifests))
                foreach (var dir in Directory.EnumerateDirectories(manifests))
                    ids.Add(Path.GetFileName(dir).ToLowerInvariant());
        }
        catch { }
        return ids.Where(id => id.Length == 32).Distinct().ToList();
    }

    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
