using System.Text.Json;
using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// Obsidian. Each window shows one vault, named in the title as
/// "Note - Vault - Obsidian 1.x"; obsidian.json maps vault names to folders.
/// Obsidian reopens a vault's last notes itself, so the item is just the vault.
/// </summary>
public sealed class ObsidianAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.Obsidian;

    public override bool CanDescribe(CapturedWindow window) =>
        window.ProcessName.Equals("Obsidian", StringComparison.OrdinalIgnoreCase) && !window.Args.Contains("--type=");

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context)
    {
        var vaultName = VaultNameFromTitle(window.Title);
        var vaultPath = vaultName == null ? null : ResolveVaultPath(vaultName, LoadVaults());
        return new WorkspaceItem
        {
            Kind = ItemKind.Obsidian,
            Name = "Obsidian",
            Title = window.Title,
            Exe = window.ExePath,
            Target = vaultPath ?? vaultName,
            Detail = vaultPath ?? (vaultName == null ? "(vault not recognised)" : $"Vault: {vaultName}"),
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
        // The protocol URI as an argument: Obsidian handles it like a click on
        // an obsidian:// link and opens (or focuses) that vault's window.
        var args = name == null ? "" : $"\"obsidian://open?vault={Uri.EscapeDataString(name)}\"";
        try
        {
            LaunchHelpers.Start(item.Exe, args);
            return Task.FromResult(new LaunchResult(true, name == null ? "opened Obsidian" : $"opened vault {name}")
            {
                WindowWait = LaunchHelpers.HeavyWait,
                Gap = LaunchHelpers.HeavyGap,
            });
        }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Failed(ex.Message)); }
    }

    /// <summary>Vault name of an item: the folder name of its path, or the bare name it fell back to.</summary>
    static string? TargetVaultName(WorkspaceItem item) =>
        item.Target == null ? null : Path.GetFileName(item.Target.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : item.Target;

    /// <summary>"Untitled - Afstuderen - Obsidian 1.15.0" → "Afstuderen"; null when the title has no vault.</summary>
    public static string? VaultNameFromTitle(string title)
    {
        var segments = TitleSegments(title);
        if (segments.Length < 2) return null;
        if (!segments[^1].StartsWith("Obsidian", StringComparison.OrdinalIgnoreCase)) return null;
        return segments[^2];
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
