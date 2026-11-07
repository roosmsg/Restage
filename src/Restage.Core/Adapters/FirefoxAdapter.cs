using System.Text.Json;
using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Firefox. A browser window is restored by starting its profile once (Firefox
/// brings its own windows and tabs back). A web app installed from Firefox
/// ("taskbar tab") is a window whose AppUserModelID is the app's id, the same
/// GUID its shortcut carries; which app that is and how to start it comes from
/// taskbartabs/taskbartabs.json in the profile. The process's command line is
/// only a fallback: all windows of a profile share one process, so it tells how
/// the first of them was started, not what each one is.
/// </summary>
public sealed class FirefoxAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.Firefox;

    /// <summary>Process name → where the browser keeps profiles.ini and Profiles\, under %APPDATA%.</summary>
    static readonly Dictionary<string, string> ProfileRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["firefox"] = @"Mozilla\Firefox",
        ["floorp"] = "Floorp",
        ["librewolf"] = "librewolf",
        ["waterfox"] = "Waterfox",
        ["zen"] = "zen",
    };

    /// <summary>What a Firefox command line says about the window it starts.</summary>
    public sealed record Args(string? ProfileName, string? ProfilePath, string? TaskbarTabId, string? Url)
    {
        public bool IsTaskbarTab => TaskbarTabId != null;

        /// <summary>Profile identity: name, else path, else "" for the default profile.</summary>
        public string ProfileKey => ProfileName ?? ProfilePath ?? "";
    }

    /// <summary>A web app installed from Firefox: what its shortcut starts.</summary>
    public sealed record TaskbarTab(string Id, string Name, string StartUrl, string ProfilePath, int ContainerId)
    {
        /// <summary>The arguments Firefox itself puts in the app's shortcut.</summary>
        public string LaunchArgs =>
            $"-taskbar-tab {Id} -new-window {LaunchHelpers.Quote(StartUrl)} -profile {LaunchHelpers.Quote(ProfilePath)} -container {ContainerId}";

        public string IconFile => Path.Combine(ProfilePath, "taskbartabs", "icons", Id + ".ico");
    }

    public override bool CanDescribe(CapturedWindow window) =>
        ProfileRoots.ContainsKey(window.ProcessName) && !window.Args.Contains("-contentproc");

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var parsed = Normalize(Parse(window.Args), DefaultProfilePath(window.ProcessName));
        var tabId = TaskbarTabId(window);
        if (tabId != null)
        {
            var tab = LoadTaskbarTabs(window.ProcessName).FirstOrDefault(t => Same(t.Id, tabId));
            if (tab != null) return TaskbarTabItem(window, tab);
            // Not in the list (older Firefox, foreign profile): the process's own arguments will do.
            if (parsed.IsTaskbarTab && Same(parsed.TaskbarTabId, tabId))
                return TaskbarTabItem(window, tabId, window.Args, HostOf(parsed.Url) ?? window.Title);
        }
        return new WorkspaceItem
        {
            Kind = ItemKind.Firefox,
            Name = window.ProcessName,
            Title = window.Title,
            Exe = window.ExePath,
            Args = ProfileArgs(parsed),
            Target = parsed.ProfileKey,
            Detail = parsed.ProfileName != null ? $"Profile: {parsed.ProfileName}"
                : parsed.ProfilePath != null ? $"Profile: {Path.GetFileName(parsed.ProfilePath)}"
                : "Default profile",
        };
    }

    public override int Match(WorkspaceItem item, CapturedWindow window)
    {
        if (!CanDescribe(window) || !Same(item.Exe, window.ExePath)) return 0;
        var liveTab = TaskbarTabId(window);
        if (item.Kind == ItemKind.FirefoxTaskbarTab)
            return liveTab != null && Same(liveTab, item.Target) ? 3 : 0;
        if (liveTab != null) return 0;
        var live = Normalize(Parse(window.Args), DefaultProfilePath(window.ProcessName));
        if (!Same(live.ProfileKey, item.Target ?? "")) return 0;
        return Same(item.Title, window.Title) ? 3 : 2;
    }

    /// <summary>One launch per profile; taskbar tabs launch singly.</summary>
    public override string? LaunchGroup(WorkspaceItem item) =>
        item.Kind == ItemKind.Firefox ? $"{item.Exe}|{item.Target}" : null;

    public override Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct)
    {
        var item = group[0];
        if (!File.Exists(item.Exe)) return Task.FromResult(LaunchResult.Failed($"not found: {item.Exe}"));
        try
        {
            LaunchHelpers.Start(item.Exe, item.Args);
            var what = item.Kind == ItemKind.FirefoxTaskbarTab ? $"web app {item.Detail}" : item.Detail ?? "Firefox";
            return Task.FromResult(new LaunchResult(true, $"started {what}")
            {
                WindowWait = LaunchHelpers.HeavyWait,
                Gap = LaunchHelpers.HeavyGap,
            });
        }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Failed(ex.Message)); }
    }

    /// <summary>The icon Firefox stored for the web app, next to its list.</summary>
    public override string? IconFile(WorkspaceItem item)
    {
        if (item.Kind != ItemKind.FirefoxTaskbarTab || item.Target == null) return null;
        var profile = Parse(item.Args).ProfilePath;
        return profile == null ? null : Path.Combine(profile, "taskbartabs", "icons", item.Target + ".ico");
    }

    /// <summary>
    /// The id of the web app a window belongs to: its AppUserModelID when that
    /// is a GUID; the command line's -taskbar-tab only when the window has no
    /// AppUserModelID at all. A window with Firefox's ordinary id is a browser
    /// window even when its process was started by a web app's shortcut.
    /// </summary>
    public static string? TaskbarTabId(CapturedWindow window)
    {
        if (window.AppUserModelId is { Length: > 0 } aumid)
            return Guid.TryParse(aumid, out var g) ? g.ToString("D") : null;
        return Parse(window.Args).TaskbarTabId;
    }

    public static WorkspaceItem TaskbarTabItem(CapturedWindow window, TaskbarTab tab) =>
        TaskbarTabItem(window, tab.Id, tab.LaunchArgs, tab.Name.Length > 0 ? tab.Name : HostOf(tab.StartUrl) ?? window.Title);

    static WorkspaceItem TaskbarTabItem(CapturedWindow window, string id, string args, string detail) => new()
    {
        Kind = ItemKind.FirefoxTaskbarTab,
        Name = $"{window.ProcessName} web app",
        Title = window.Title,
        Exe = window.ExePath,
        Args = args,
        Target = id,
        Detail = detail,
    };

    static string? HostOf(string? url) =>
        url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    /// <summary>Arguments that start the given profile: -P for a named one, -profile for a path, none for the default.</summary>
    public static string ProfileArgs(Args parsed) =>
        parsed.ProfileName != null ? $"-P {LaunchHelpers.Quote(parsed.ProfileName)}"
        : parsed.ProfilePath != null ? $"-profile {LaunchHelpers.Quote(parsed.ProfilePath)}"
        : "";

    /// <summary>Reads the switches Firefox cares about; ignores the rest.</summary>
    public static Args Parse(string args)
    {
        string? name = null, path = null, tab = null, url = null;
        var tokens = LaunchHelpers.Tokenize(args);
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i].TrimStart('-').ToLowerInvariant();
            if (!tokens[i].StartsWith('-')) continue;
            string? next = i + 1 < tokens.Count ? tokens[i + 1] : null;
            switch (t)
            {
                case "p": name = next; i++; break;
                case "profile": path = next; i++; break;
                case "taskbar-tab": tab = next; i++; break;
                case "new-window": case "url": case "new-tab": url ??= next; i++; break;
                case "container": i++; break;
            }
        }
        return new Args(name, path, tab, url);
    }

    /// <summary>
    /// A -profile path that is the default profile means the same as no profile
    /// switch at all. Web app shortcuts always pass the path, so without this a
    /// Firefox started by one would never match a workspace's "default profile".
    /// </summary>
    public static Args Normalize(Args parsed, string? defaultProfilePath) =>
        parsed.ProfilePath != null && defaultProfilePath != null && SamePath(parsed.ProfilePath, defaultProfilePath)
            ? parsed with { ProfilePath = null }
            : parsed;

    static bool SamePath(string a, string b)
    {
        try { return Same(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b))); }
        catch { return Same(a, b); }
    }

    /// <summary>The default profile's directory from profiles.ini, or null when unknown.</summary>
    public static string? DefaultProfilePath(string processName)
    {
        if (!ProfileRoots.TryGetValue(processName, out var rel)) return null;
        var root = Path.Combine(AppData, rel);
        try
        {
            var ini = Path.Combine(root, "profiles.ini");
            return File.Exists(ini) ? DefaultProfileFromIni(File.ReadAllText(ini), root) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// profiles.ini: the first [Install…] section's Default wins (that is what
    /// Firefox starts), else the [Profile…] section flagged Default=1. Relative
    /// paths are relative to the ini's directory.
    /// </summary>
    public static string? DefaultProfileFromIni(string ini, string root)
    {
        string? section = null, installDefault = null, flagged = null, path = null;
        bool isDefault = false, isRelative = true;
        void Flush()
        {
            if (flagged == null && section != null && section.StartsWith("Profile", StringComparison.OrdinalIgnoreCase) && isDefault && path != null)
                flagged = isRelative ? Path.Combine(root, path.Replace('/', '\\')) : path;
        }
        foreach (var raw in ini.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                Flush();
                section = line.Trim('[', ']');
                path = null; isDefault = false; isRelative = true;
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (section != null && section.StartsWith("Install", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("Default", StringComparison.OrdinalIgnoreCase)) installDefault ??= value;
                continue;
            }
            if (key.Equals("Path", StringComparison.OrdinalIgnoreCase)) path = value;
            else if (key.Equals("IsRelative", StringComparison.OrdinalIgnoreCase)) isRelative = value == "1";
            else if (key.Equals("Default", StringComparison.OrdinalIgnoreCase)) isDefault = value == "1";
        }
        Flush();
        if (installDefault != null)
            return Path.IsPathRooted(installDefault) ? installDefault : Path.Combine(root, installDefault.Replace('/', '\\'));
        return flagged;
    }

    /// <summary>The web apps of every profile of the browser, from taskbartabs/taskbartabs.json.</summary>
    public static IReadOnlyList<TaskbarTab> LoadTaskbarTabs(string processName)
    {
        var result = new List<TaskbarTab>();
        if (!ProfileRoots.TryGetValue(processName, out var rel)) return result;
        var profiles = Path.Combine(AppData, rel, "Profiles");
        try
        {
            if (!Directory.Exists(profiles)) return result;
            foreach (var profile in Directory.EnumerateDirectories(profiles))
            {
                var file = Path.Combine(profile, "taskbartabs", "taskbartabs.json");
                if (File.Exists(file)) result.AddRange(ParseTaskbarTabs(File.ReadAllText(file), profile));
            }
        }
        catch { }
        return result;
    }

    /// <summary>Reads Firefox's list: {"version": n, "taskbarTabs": [{id, name, startUrl, userContextId, …}]}.</summary>
    public static List<TaskbarTab> ParseTaskbarTabs(string json, string profilePath)
    {
        var result = new List<TaskbarTab>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("taskbarTabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array) return result;
            foreach (var t in tabs.EnumerateArray())
            {
                var id = Str(t, "id");
                var url = Str(t, "startUrl");
                if (id == null || url == null) continue;
                var container = t.TryGetProperty("userContextId", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;
                result.Add(new TaskbarTab(id, Str(t, "name") ?? "", url, profilePath, container));
            }
        }
        catch { }
        return result;
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
}
