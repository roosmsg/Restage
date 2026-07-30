using System.IO;
using System.Text.Json;
using Rewake.Models;

namespace Rewake.Services;

/// <summary>
/// Names and favourite flags for captures, held in capture-meta.json next to the
/// snapshots instead of inside them.
///
/// Writing into a snapshot file would move its LastWriteTime, and that timestamp
/// is exactly what the sidebar labels and sorts rows by — naming a capture would
/// silently change the time it shows and jump it to the top of the list.
///
/// Entries are keyed by file name rather than full path, so the metadata stays
/// attached if the data folder ever moves.
/// </summary>
public static class CaptureMetaService
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // Capture (and its pruning) runs on background threads while the UI thread
    // renames and stars, so every read and write goes through one lock.
    static readonly object Gate = new();
    static Dictionary<string, CaptureMeta>? _map;

    static Dictionary<string, CaptureMeta> Map()
    {
        if (_map != null) return _map;
        try
        {
            if (File.Exists(Paths.CaptureMetaFile))
            {
                var read = JsonSerializer.Deserialize<Dictionary<string, CaptureMeta>>(
                    File.ReadAllText(Paths.CaptureMetaFile));
                if (read != null) _map = new(read, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return _map ??= new(StringComparer.OrdinalIgnoreCase);
    }

    static string Key(string capturePath) => Path.GetFileName(capturePath);

    // Keys are file names; this turns one back into the file it describes so a
    // save can tell which entries have outlived their capture.
    static string PathFor(string key) =>
        key.Equals(Path.GetFileName(Paths.LastSession), StringComparison.OrdinalIgnoreCase)
            ? Paths.LastSession
            : Path.Combine(Paths.SnapDir, key);

    /// <summary>Name and favourite flag for a capture, or null if it has neither.</summary>
    public static CaptureMeta? Get(string capturePath)
    {
        lock (Gate) return Map().TryGetValue(Key(capturePath), out var m) ? m : null;
    }

    /// <summary>Asked by the pruner before it deletes anything.</summary>
    public static bool IsFavorite(string capturePath)
    {
        lock (Gate) return Map().TryGetValue(Key(capturePath), out var m) && m.Favorite;
    }

    public static void SetFavorite(string capturePath, bool favorite) =>
        Update(capturePath, m => m.Favorite = favorite);

    /// <summary>Sets the display name; null or blank clears it back to the time.</summary>
    public static void SetName(string capturePath, string? name) =>
        Update(capturePath, m => m.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim());

    /// <summary>Drops a capture's metadata — call when its file is deleted.</summary>
    public static void Forget(string capturePath)
    {
        lock (Gate)
        {
            if (Map().Remove(Key(capturePath))) Save();
        }
    }

    static void Update(string capturePath, Action<CaptureMeta> change)
    {
        lock (Gate)
        {
            var key = Key(capturePath);
            var map = Map();
            if (!map.TryGetValue(key, out var meta)) map[key] = meta = new CaptureMeta();
            change(meta);
            Save();
        }
    }

    // Caller holds the lock. Entries whose capture is gone, or that no longer
    // say anything, are dropped here rather than accumulating forever — a named
    // capture can still be pruned away for not being a favourite.
    static void Save()
    {
        var map = _map!;
        foreach (var key in map.Keys.ToList())
            if (map[key].IsEmpty || !File.Exists(PathFor(key)))
                map.Remove(key);
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllText(Paths.CaptureMetaFile, JsonSerializer.Serialize(map, JsonOpts));
        }
        catch { }
    }
}
