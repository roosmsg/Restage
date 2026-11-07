using Restage.Adapters;
using Restage.Models;
using Restage.Native;

namespace Restage.Services;

/// <summary>
/// Turns the desktop as it stands into a workspace: every user window through
/// its adapter, the open Explorer folders, the current virtual desktop,
/// wallpaper and monitor arrangement, and a screenshot for the gallery.
/// </summary>
public static class WorkspaceCaptureService
{
    public sealed record Result(Workspace Workspace, byte[]? ThumbnailPng, DisplayProfile Profile);

    /// <summary>
    /// Captures into a new workspace, or refreshes <paramref name="existing"/>:
    /// items are re-detected, and an old item that is recognised again keeps
    /// its id, policy, enabled flag and its placements for other display
    /// profiles, so docking and undocking never throws away the other layout.
    /// </summary>
    public static Result Capture(string name, Workspace? existing = null, bool takeThumbnail = true)
    {
        Win32.EnsurePerMonitorDpi();
        var profile = DisplayProfiles.Current();
        var windows = WindowEnumerator.Enumerate();
        var context = new CaptureContext { Windows = windows, ProfileId = profile.Id };

        var fresh = new List<WorkspaceItem>();
        foreach (var w in windows)
        {
            var adapter = AdapterRegistry.For(w);
            var item = adapter.Describe(w, context);
            item.Placements[profile.Id] = w.Placement;
            fresh.Add(item);
        }
        foreach (var folder in ExplorerWindows.ListSta())
        {
            var item = ExplorerAdapter.FromFolder(folder.Path);
            item.Placements[profile.Id] = WindowPlacer.Capture(folder.Handle);
            fresh.Add(item);
        }

        var ws = existing ?? new Workspace { CreatedAt = DateTime.Now };
        ws.Name = string.IsNullOrWhiteSpace(name) ? ws.Name : name.Trim();
        if (existing != null) Merge(existing.Items, fresh, profile.Id);
        ws.Items = fresh;
        ws.Remember(profile);
        ws.Desktop = VirtualDesktops.Current() ?? ws.Desktop;
        ws.Wallpaper = DesktopWallpaper.Current() ?? ws.Wallpaper;

        var thumbnail = takeThumbnail ? Thumbnails.CaptureVirtualScreen() : null;
        return new Result(ws, thumbnail, profile);
    }

    /// <summary>Carries identity, flags and other profiles' placements from old items into matching fresh ones.</summary>
    internal static void Merge(List<WorkspaceItem> old, List<WorkspaceItem> fresh, string currentProfileId)
    {
        var unused = new List<WorkspaceItem>(old);
        foreach (var item in fresh)
        {
            var match = unused.FirstOrDefault(o => SameIdentity(o, item));
            if (match == null) continue;
            unused.Remove(match);
            item.Id = match.Id;
            item.Policy = match.Policy;
            item.Enabled = match.Enabled;
            item.OpenContent = match.OpenContent;
            foreach (var (profileId, placement) in match.Placements)
                if (profileId != currentProfileId) item.Placements.TryAdd(profileId, placement);
        }
    }

    static bool SameIdentity(WorkspaceItem a, WorkspaceItem b)
    {
        if (a.Kind != b.Kind || !string.Equals(a.Exe, b.Exe, StringComparison.OrdinalIgnoreCase)) return false;
        if (a.Target != null || b.Target != null)
            return string.Equals(a.Target, b.Target, StringComparison.OrdinalIgnoreCase);
        return string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
    }
}
