using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Restage.Models;

namespace Restage.Services;

/// <summary>
/// Workspaces on disk: one folder per workspace under %LOCALAPPDATA%\Restage\workspaces,
/// holding workspace.json and thumbnail.png. Plain files, so they can be read,
/// backed up, diffed and repaired by hand.
/// </summary>
public static class WorkspaceStore
{
    public const string FileName = "workspace.json";
    public const string ThumbnailName = "thumbnail.png";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Root => Paths.WorkspacesDir;

    public static string DirectoryFor(string id) => Path.Combine(Root, id);
    public static string FileFor(string id) => Path.Combine(DirectoryFor(id), FileName);
    public static string ThumbnailFor(string id) => Path.Combine(DirectoryFor(id), ThumbnailName);

    /// <summary>Every readable workspace, most recently updated first.</summary>
    public static List<Workspace> List()
    {
        var list = new List<Workspace>();
        if (!Directory.Exists(Root)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var ws = Load(Path.GetFileName(dir));
            if (ws != null) list.Add(ws);
        }
        return list.OrderByDescending(w => w.UpdatedAt).ToList();
    }

    public static Workspace? Load(string id)
    {
        try
        {
            var file = FileFor(id);
            if (!File.Exists(file)) return null;
            var ws = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(file), JsonOptions);
            if (ws == null) return null;
            ws.UpgradeRemovedPolicies();
            ws.Id = id;
            ws.Directory = DirectoryFor(id);
            return ws;
        }
        catch { return null; }
    }

    /// <summary>Finds a workspace by id or (case-insensitively) by name.</summary>
    public static Workspace? Find(string idOrName) =>
        Load(idOrName) ?? List().FirstOrDefault(w => string.Equals(w.Name, idOrName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Writes the workspace (and its thumbnail, when given), assigning an id on
    /// first save. <paramref name="touch"/> false keeps UpdatedAt as it is, for
    /// bookkeeping writes that are not edits.
    /// </summary>
    public static void Save(Workspace ws, byte[]? thumbnailPng = null, bool touch = true)
    {
        if (string.IsNullOrWhiteSpace(ws.Id)) ws.Id = NewId(ws.Name);
        if (touch || ws.UpdatedAt == default) ws.UpdatedAt = DateTime.Now;
        if (ws.CreatedAt == default) ws.CreatedAt = ws.UpdatedAt;
        var dir = DirectoryFor(ws.Id);
        Directory.CreateDirectory(dir);
        ws.Directory = dir;

        // Write-then-rename, so a crash mid-write never leaves a half workspace.
        var file = FileFor(ws.Id);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ws, JsonOptions));
        File.Move(tmp, file, overwrite: true);
        if (thumbnailPng is { Length: > 0 }) File.WriteAllBytes(ThumbnailFor(ws.Id), thumbnailPng);
    }

    /// <summary>Records that the workspace was started now; not an edit, so UpdatedAt stays.</summary>
    public static void MarkOpened(Workspace ws)
    {
        ws.LastOpenedAt = DateTime.Now;
        Save(ws, touch: false);
    }

    public static void Delete(string id)
    {
        var dir = DirectoryFor(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    /// <summary>A readable, unique folder name: slug of the name plus a short random suffix.</summary>
    public static string NewId(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 32) slug = slug[..32].TrimEnd('-');
        if (slug.Length == 0) slug = "workspace";
        var suffix = Guid.NewGuid().ToString("N")[..6];
        return $"{slug}-{suffix}";
    }
}
