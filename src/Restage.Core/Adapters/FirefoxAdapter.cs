using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Firefox. A browser window is restored by starting its profile once (Firefox
/// brings its own windows and tabs back); a web app pinned to the taskbar runs
/// as its own firefox.exe with explicit arguments and is relaunched with those.
/// </summary>
public sealed class FirefoxAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.Firefox;

    static readonly HashSet<string> Processes = new(StringComparer.OrdinalIgnoreCase)
    {
        "firefox", "floorp", "librewolf", "waterfox", "zen",
    };

    /// <summary>What a Firefox command line says about the window it starts.</summary>
    public sealed record Args(string? ProfileName, string? ProfilePath, string? TaskbarTabId, string? Url)
    {
        public bool IsTaskbarTab => TaskbarTabId != null;

        /// <summary>Profile identity: name, else path, else "" for the default profile.</summary>
        public string ProfileKey => ProfileName ?? ProfilePath ?? "";
    }

    public override bool CanDescribe(CapturedWindow window) =>
        Processes.Contains(window.ProcessName) && !window.Args.Contains("-contentproc");

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var parsed = Parse(window.Args);
        if (parsed.IsTaskbarTab)
        {
            return new WorkspaceItem
            {
                Kind = ItemKind.FirefoxTaskbarTab,
                Name = $"{window.ProcessName} web app",
                Title = window.Title,
                Exe = window.ExePath,
                Args = window.Args,              // verbatim: Firefox's own shortcut arguments
                Target = parsed.TaskbarTabId,
                Detail = parsed.Url is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Host : window.Title,
            };
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
        var live = Parse(window.Args);
        if (item.Kind == ItemKind.FirefoxTaskbarTab)
            return live.IsTaskbarTab && Same(live.TaskbarTabId, item.Target) ? 3 : 0;
        if (live.IsTaskbarTab) return 0;
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
}
