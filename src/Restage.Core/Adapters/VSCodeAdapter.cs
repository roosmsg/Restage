using System.Text.Json;
using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Visual Studio Code (and Insiders / VSCodium). VS Code restores its own
/// editors per folder, so an item only needs the folder or .code-workspace a
/// window shows. Window titles carry the folder's name, not its path; the path
/// comes from VS Code's workspaceStorage, where every folder it has opened has
/// a workspace.json and a state file whose write time says how recently that
/// window was alive.
/// </summary>
public sealed class VSCodeAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.VSCode;

    static readonly Dictionary<string, string> UserDataDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code"] = "Code",
        ["Code - Insiders"] = "Code - Insiders",
        ["VSCodium"] = "VSCodium",
    };

    /// <summary>A folder or workspace VS Code has opened before.</summary>
    public sealed record KnownWorkspace(string Path, DateTime LastActive)
    {
        public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    }

    public override bool CanDescribe(CapturedWindow window) =>
        UserDataDirs.ContainsKey(window.ProcessName) && !window.Args.Contains("--type=");

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var known = LoadKnownWorkspaces(UserDataDir(window));
        var target = ResolveTarget(window.Title, known);
        return new WorkspaceItem
        {
            Kind = ItemKind.VSCode,
            Name = window.ProcessName,
            Title = window.Title,
            Exe = window.ExePath,
            Target = target,
            Detail = target ?? "(folder not recognised; opens VS Code)",
        };
    }

    public override int Match(WorkspaceItem item, CapturedWindow window)
    {
        if (!Same(item.Exe, window.ExePath) || window.Args.Contains("--type=")) return 0;
        if (item.Target == null) return Same(item.Title, window.Title) ? 2 : 1;
        var name = new KnownWorkspace(item.Target, default).Name;
        return TitleSegments(window.Title).Any(s => Same(s, name)) ? 3 : 0;
    }

    public override Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct)
    {
        var item = group[0];
        if (!File.Exists(item.Exe)) return Task.FromResult(LaunchResult.Failed($"not found: {item.Exe}"));
        if (item.Target != null && !Directory.Exists(item.Target) && !File.Exists(item.Target))
            return Task.FromResult(LaunchResult.Failed($"folder missing: {item.Target}"));
        // -n: always a new window, so one launch per captured window holds even
        // when VS Code's "open folders in new window" setting says otherwise.
        var args = item.Target == null ? "-n" : $"-n \"{item.Target}\"";
        try
        {
            LaunchHelpers.Start(item.Exe, args);
            return Task.FromResult(new LaunchResult(true, $"opened {item.Target ?? "a new window"}")
            {
                WindowWait = LaunchHelpers.HeavyWait,
                Gap = LaunchHelpers.HeavyGap,
            });
        }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Failed(ex.Message)); }
    }

    static string UserDataDir(CapturedWindow window)
    {
        var tokens = LaunchHelpers.Tokenize(window.Args);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].StartsWith("--user-data-dir=", StringComparison.OrdinalIgnoreCase))
                return tokens[i]["--user-data-dir=".Length..];
            if (tokens[i].Equals("--user-data-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                return tokens[i + 1];
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            UserDataDirs.TryGetValue(window.ProcessName, out var d) ? d : "Code");
    }

    /// <summary>Every folder/workspace in User\workspaceStorage, with the time its state was last written.</summary>
    public static List<KnownWorkspace> LoadKnownWorkspaces(string userDataDir)
    {
        var list = new List<KnownWorkspace>();
        var root = Path.Combine(userDataDir, "User", "workspaceStorage");
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                var meta = Path.Combine(dir, "workspace.json");
                if (!File.Exists(meta)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(meta));
                var uri = doc.RootElement.TryGetProperty("folder", out var f) ? f.GetString()
                    : doc.RootElement.TryGetProperty("workspace", out var w) ? w.GetString()
                    : null;
                if (uri == null || LocalPathFromUri(uri) is not { } localPath) continue;
                var state = Path.Combine(dir, "state.vscdb");
                var when = File.Exists(state) ? File.GetLastWriteTime(state) : Directory.GetLastWriteTime(dir);
                list.Add(new KnownWorkspace(localPath, when));
            }
            catch { }
        }
        return list;
    }

    /// <summary>
    /// "file:///c%3A/Users/x/Projects/app" → "C:\Users\x\Projects\app". VS Code
    /// percent-encodes the drive colon, which Uri.LocalPath does not undo, so the
    /// path is decoded by hand. Null for anything that is not a local file URI.
    /// </summary>
    public static string? LocalPathFromUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeFile) return null;
        var path = Uri.UnescapeDataString(parsed.AbsolutePath);
        if (parsed.Host.Length > 0) return $@"\\{parsed.Host}{path.Replace('/', '\\')}";   // UNC
        if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':')
            path = char.ToUpperInvariant(path[1]) + path[2..];   // VS Code lower-cases the drive letter
        path = path.Replace('/', '\\');
        return path.Length == 2 ? path + "\\" : path.TrimEnd('\\');
    }

    /// <summary>
    /// The folder a window shows, from its title. The title (in any format the
    /// user configured) contains the folder name as one " - " segment; among
    /// known folders with that name the most recently active wins. Null when no
    /// segment matches.
    /// </summary>
    public static string? ResolveTarget(string title, IReadOnlyList<KnownWorkspace> known)
    {
        var segments = TitleSegments(title);
        // Walk from the end: the default title puts the folder after the file.
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var seg = segments[i];
            var hit = known
                .Where(k => Same(k.Name, seg))
                .OrderByDescending(k => k.LastActive)
                .FirstOrDefault();
            if (hit != null) return hit.Path;
        }
        return null;
    }
}
