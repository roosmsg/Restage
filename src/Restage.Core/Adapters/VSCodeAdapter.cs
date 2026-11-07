using System.Text.Json;
using System.Text.RegularExpressions;
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
        return Locate(window.Title, item.Target).Score;
    }

    public override Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct)
    {
        var item = group[0];
        if (!File.Exists(item.Exe)) return Task.FromResult(LaunchResult.Failed($"not found: {item.Exe}"));
        if (item.Target != null && !Directory.Exists(item.Target) && !File.Exists(item.Target))
            return Task.FromResult(LaunchResult.Failed($"folder missing: {item.Target}"));
        // -n: always a new window, so one launch per captured window holds even
        // when VS Code's "open folders in new window" setting says otherwise.
        // With OpenContent off, VS Code just starts and restores its own windows.
        var args = !item.OpenContent ? "" : item.Target == null ? "-n" : $"-n \"{item.Target}\"";
        try
        {
            // VS Code 1.145 (Code.exe at the install root, the app in a
            // per-commit folder) ignores a direct `Code.exe <folder>` while an
            // instance is running: no window, no error. The CLI next to it
            // (bin\code.cmd, what the `code` command runs) hands the folder to
            // the running instance or starts one, so that is the launch path;
            // the exe stays the fallback for installs without a CLI.
            var cli = CliFor(item.Exe);
            if (cli != null) LaunchHelpers.StartHidden("cmd.exe", $"/d /s /c \"\"{cli}\" {args}\"");
            else LaunchHelpers.Start(item.Exe, args);
            return Task.FromResult(new LaunchResult(true, $"opened {item.Target ?? "a new window"}")
            {
                WindowWait = LaunchHelpers.HeavyWait,
                Gap = LaunchHelpers.HeavyGap,
            });
        }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Failed(ex.Message)); }
    }

    /// <summary>
    /// The CLI script of the install the executable belongs to (bin\code.cmd
    /// next to Code.exe; code-insiders.cmd and codium.cmd for the others), or
    /// null when the install has none.
    /// </summary>
    public static string? CliFor(string exePath)
    {
        var bin = Path.Combine(Path.GetDirectoryName(exePath) ?? "", "bin");
        if (!Directory.Exists(bin)) return null;
        var preferred = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant() switch
        {
            "code" => "code.cmd",
            "code - insiders" => "code-insiders.cmd",
            "vscodium" => "codium.cmd",
            _ => null,
        };
        if (preferred != null && File.Exists(Path.Combine(bin, preferred))) return Path.Combine(bin, preferred);
        return Directory.EnumerateFiles(bin, "*.cmd").FirstOrDefault();
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
        // Uri yields "/c:/…" for an escaped drive colon and "c:/…" for a literal one.
        if (path.Length >= 3 && path[0] == '/' && char.IsLetter(path[1]) && path[2] == ':') path = path[1..];
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            path = char.ToUpperInvariant(path[0]) + path[1..];   // VS Code lower-cases the drive letter
        path = path.Replace('/', '\\');
        return path.Length == 2 ? path + "\\" : path.TrimEnd('\\');
    }

    /// <summary>
    /// The folder a window shows, from its title. The title (in any format the
    /// user configured) contains the folder's name or path; among known folders
    /// the best <see cref="Locate"/> hit wins: a whole " - " segment over a
    /// word inside one, then the later segment (the default format puts the
    /// active file before the folder) or the longer name (more specific than a
    /// view or language name that shares its segment), then the most recently
    /// active. Null when nothing matches.
    /// </summary>
    public static string? ResolveTarget(string title, IReadOnlyList<KnownWorkspace> known)
    {
        return known
            .Select(k => (Workspace: k, Hit: Locate(title, k.Path)))
            .Where(c => c.Hit.Score > 0)
            .OrderByDescending(c => c.Hit.Score)
            .ThenByDescending(c => c.Hit.Score == SegmentScore ? c.Hit.Position : c.Workspace.Name.Length)
            .ThenByDescending(c => c.Hit.Score == SegmentScore ? c.Workspace.Name.Length : c.Hit.Position)
            .ThenByDescending(c => c.Workspace.LastActive)
            .Select(c => c.Workspace.Path)
            .FirstOrDefault();
    }

    const int SegmentScore = 3, WordScore = 2;

    /// <summary>
    /// Where a folder shows up in a window title, as (score, position). The
    /// default title format ("${activeEditorShort} - ${rootName} - Visual
    /// Studio Code") gives the folder a " - " segment of its own: score 3.
    /// Custom formats often keep other variables in the folder's segment
    /// ("${rootName} ${focusedView} ${activeEditorLanguageId}" shows as
    /// "afstuderen Explorer markdown"), so the folder's name or full path as
    /// a whole word anywhere in the title still counts: score 2. Score 0 when
    /// the title has neither. A word is bounded by anything but a letter or
    /// digit, so "app" is not found in "application".
    /// </summary>
    public static (int Score, int Position) Locate(string title, string folderPath)
    {
        var best = (Score: 0, Position: -1);
        var name = new KnownWorkspace(folderPath, default).Name;
        foreach (var needle in new[] { folderPath, name })
        {
            if (needle.Length == 0) continue;
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(needle)}(?![\p{{L}}\p{{N}}])";
            foreach (Match m in Regex.Matches(title, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var before = title[..m.Index].TrimEnd();
                var after = title[(m.Index + m.Length)..].TrimStart();
                var wholeSegment = (before.Length == 0 || before.EndsWith(" -", StringComparison.Ordinal))
                               && (after.Length == 0 || after.StartsWith("- ", StringComparison.Ordinal));
                var hit = (Score: wholeSegment ? SegmentScore : WordScore, Position: m.Index);
                if (hit.Score > best.Score || (hit.Score == best.Score && hit.Position > best.Position)) best = hit;
            }
        }
        return best;
    }
}
