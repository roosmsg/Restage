using System.Text.Json;
using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Obsidian. Each window shows one vault and one active note, named in the
/// title as "Note - Vault - Obsidian 1.x"; obsidian.json maps vault names to
/// folders, and the note's file is found inside the vault by name. Restoring
/// opens the vault with that note through obsidian://open, also when the vault
/// is already open, so the right note comes to the front either way.
/// </summary>
public sealed class ObsidianAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.Obsidian;

    static readonly string[] NoteExtensions = { ".md", ".canvas", ".base" };
    static readonly string[] SkippedFolders = { ".obsidian", ".trash", ".git" };

    public override bool CanDescribe(CapturedWindow window) =>
        window.ProcessName.Equals("Obsidian", StringComparison.OrdinalIgnoreCase) && !window.Args.Contains("--type=");

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var vaultName = VaultNameFromTitle(window.Title);
        var vaultPath = vaultName == null ? null : ResolveVaultPath(vaultName, LoadVaults());
        var noteName = NoteNameFromTitle(window.Title);
        var notePath = noteName != null && vaultPath != null ? ResolveNotePath(vaultPath, noteName) : null;
        var target = vaultPath ?? vaultName;
        return new WorkspaceItem
        {
            Kind = ItemKind.Obsidian,
            Name = "Obsidian",
            Title = window.Title,
            Exe = window.ExePath,
            Target = target,
            Args = target == null ? "" : OpenUri(VaultName(target), notePath ?? noteName),
            Detail = vaultPath == null
                ? (vaultName == null ? "(vault not recognised)" : $"Vault: {vaultName}")
                : noteName == null ? vaultPath : $"{vaultPath} · {noteName}",
        };
    }

    public override int Match(WorkspaceItem item, CapturedWindow window)
    {
        if (!CanDescribe(window)) return 0;
        var name = TargetVaultName(item);
        if (name == null) return 1;
        return Same(VaultNameFromTitle(window.Title), name) ? 3 : 0;
    }

    public override Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct)
    {
        var item = group[0];
        if (!File.Exists(item.Exe)) return Task.FromResult(LaunchResult.Failed($"not found: {item.Exe}"));
        var name = TargetVaultName(item);
        try
        {
            LaunchHelpers.Start(item.Exe, LaunchArgs(item));
            return Task.FromResult(new LaunchResult(true, name == null ? "opened Obsidian" : $"opened vault {name}")
            {
                WindowWait = LaunchHelpers.HeavyWait,
                Gap = LaunchHelpers.HeavyGap,
            });
        }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Failed(ex.Message)); }
    }

    /// <summary>
    /// The vault is already open: send the open URI anyway so its window shows
    /// the saved note. Obsidian routes the URI to the running instance.
    /// </summary>
    public override Task ReuseAsync(WorkspaceItem item, CapturedWindow window, RestoreContext context, CancellationToken ct)
    {
        if (item.Args.Contains("&file=", StringComparison.Ordinal) && File.Exists(item.Exe))
        {
            try { LaunchHelpers.Start(item.Exe, LaunchArgs(item)); }
            catch (Exception ex) { context.Log?.Invoke($"Obsidian: could not reopen the note: {ex.Message}"); }
        }
        return Task.CompletedTask;
    }

    /// <summary>The protocol URI as an argument; Obsidian handles it like a click on an obsidian:// link.</summary>
    static string LaunchArgs(WorkspaceItem item)
    {
        if (item.Args.Length > 0) return $"\"{item.Args}\"";
        var name = TargetVaultName(item);
        return name == null ? "" : $"\"{OpenUri(name, null)}\"";
    }

    /// <summary>obsidian://open?vault=…[&amp;file=…], with the note as a vault-relative path.</summary>
    public static string OpenUri(string vaultName, string? note)
    {
        var uri = $"obsidian://open?vault={Uri.EscapeDataString(vaultName)}";
        if (!string.IsNullOrWhiteSpace(note)) uri += $"&file={Uri.EscapeDataString(note.Replace('\\', '/'))}";
        return uri;
    }

    static string VaultName(string target) =>
        Path.GetFileName(target.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : target;

    /// <summary>Vault name of an item: the folder name of its path, or the bare name it fell back to.</summary>
    static string? TargetVaultName(WorkspaceItem item) => item.Target == null ? null : VaultName(item.Target);

    /// <summary>"Untitled - Afstuderen - Obsidian 1.15.0" → "Afstuderen"; null when the title has no vault.</summary>
    public static string? VaultNameFromTitle(string title)
    {
        var segments = TitleSegments(title);
        if (segments.Length < 2) return null;
        if (!segments[^1].StartsWith("Obsidian", StringComparison.OrdinalIgnoreCase)) return null;
        return segments[^2];
    }

    /// <summary>
    /// "My - Note - Vault - Obsidian 1.15.0" → "My - Note": everything before the
    /// vault segment. Null when no note is open (title "Vault - Obsidian …").
    /// </summary>
    public static string? NoteNameFromTitle(string title)
    {
        var segments = TitleSegments(title);
        if (segments.Length < 3) return null;
        if (!segments[^1].StartsWith("Obsidian", StringComparison.OrdinalIgnoreCase)) return null;
        var note = string.Join(" - ", segments[..^2]);
        return note.Length == 0 ? null : note;
    }

    /// <summary>
    /// The note's path relative to the vault: a file named after the note
    /// (.md first, then .canvas, .base), the most recently modified one when
    /// several folders have one. Null when nothing matches.
    /// </summary>
    public static string? ResolveNotePath(string vaultPath, string noteName)
    {
        try
        {
            if (!Directory.Exists(vaultPath)) return null;
            var root = Path.GetFullPath(vaultPath).TrimEnd('\\', '/');
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            var candidates = NoteExtensions
                .SelectMany(ext => Directory.EnumerateFiles(root, noteName + ext, options))
                .Where(f => !SkippedFolders.Any(s => f.Contains($"{Path.DirectorySeparatorChar}{s}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                .Select(f => (Path: f, Rank: Array.IndexOf(NoteExtensions, Path.GetExtension(f).ToLowerInvariant()), Modified: File.GetLastWriteTimeUtc(f)))
                .OrderBy(c => c.Rank).ThenByDescending(c => c.Modified)
                .ToList();
            if (candidates.Count == 0) return null;
            return Path.GetRelativePath(root, candidates[0].Path).Replace('\\', '/');
        }
        catch { return null; }
    }

    /// <summary>Vault folders from %APPDATA%\obsidian\obsidian.json (open vaults first).</summary>
    public static List<string> LoadVaults()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obsidian", "obsidian.json");
        return LoadVaults(File.Exists(file) ? File.ReadAllText(file) : null);
    }

    public static List<string> LoadVaults(string? obsidianJson)
    {
        var result = new List<(string Path, bool Open, long Ts)>();
        if (string.IsNullOrWhiteSpace(obsidianJson)) return new();
        try
        {
            using var doc = JsonDocument.Parse(obsidianJson);
            if (!doc.RootElement.TryGetProperty("vaults", out var vaults)) return new();
            foreach (var v in vaults.EnumerateObject())
            {
                if (!v.Value.TryGetProperty("path", out var p) || p.GetString() is not { Length: > 0 } path) continue;
                var open = v.Value.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.True;
                var ts = v.Value.TryGetProperty("ts", out var t) && t.TryGetInt64(out var l) ? l : 0;
                result.Add((path, open, ts));
            }
        }
        catch { }
        return result.OrderByDescending(r => r.Open).ThenByDescending(r => r.Ts).Select(r => r.Path).ToList();
    }

    /// <summary>The vault folder whose name matches; the first (most recently open) one wins.</summary>
    public static string? ResolveVaultPath(string vaultName, IReadOnlyList<string> vaultPaths) =>
        vaultPaths.FirstOrDefault(p => Same(Path.GetFileName(p.TrimEnd('\\', '/')), vaultName));
}
