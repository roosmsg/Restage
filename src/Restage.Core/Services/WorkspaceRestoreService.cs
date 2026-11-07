using Restage.Adapters;
using Restage.Models;
using Restage.Native;

namespace Restage.Services;

/// <summary>Settings for one restore run.</summary>
public sealed class RestoreOptions
{
    /// <summary>Launch even when a matching window is already open (treats IfNotRunning as AlwaysNew).</summary>
    public bool Force { get; init; }

    /// <summary>Apply the workspace's wallpaper once on its desktop, when the workspace asks for it.</summary>
    public bool ApplyWallpaper { get; init; } = true;

    /// <summary>
    /// Asked to get the user onto the workspace's desktop (there is no public
    /// API to switch). Returns true once the desktop is current; false to carry
    /// on where we are. Null skips the desktop step entirely.
    /// </summary>
    public Func<DesktopRef, CancellationToken, Task<bool>>? SwitchDesktop { get; init; }

    /// <summary>
    /// Raise the workspace's windows above everything else once they are placed,
    /// the first item on top and active: switching to a workspace whose apps are
    /// already open. Windows left minimized stay minimized.
    /// </summary>
    public bool BringToFront { get; init; }
}

/// <summary>Progress of one item (or of the run when <see cref="ItemId"/> is null).</summary>
public sealed record RestoreEvent(string? ItemId, RestoreStatus Status, string Message);

/// <summary>
/// Brings a workspace back: desktop, wallpaper, then every enabled item through
/// its adapter. Items that share a launch group start together; existing windows
/// are reused per the item's policy; each window found is moved to its saved
/// placement for the current display profile. One failure never stops the rest.
/// </summary>
public static class WorkspaceRestoreService
{
    public static async Task RestoreAsync(Workspace ws, RestoreOptions options, IProgress<RestoreEvent> progress, CancellationToken ct)
    {
        Win32.EnsurePerMonitorDpi();
        void Report(string? id, RestoreStatus s, string m) => progress.Report(new RestoreEvent(id, s, m));

        // ---- Desktop ---------------------------------------------------
        if (ws.Desktop != null && options.SwitchDesktop != null)
        {
            var live = VirtualDesktops.Resolve(ws.Desktop);
            if (live == null)
                Report(null, RestoreStatus.Skipped, $"Desktop \"{ws.Desktop.DisplayName}\" no longer exists; restoring on the current one.");
            else if (live.Id is { } id && !VirtualDesktops.IsCurrent(id))
            {
                var switched = await options.SwitchDesktop(live, ct);
                Report(null, switched ? RestoreStatus.Opened : RestoreStatus.Skipped,
                    switched ? $"On desktop \"{live.DisplayName}\"." : "Staying on the current desktop.");
            }
        }
        ct.ThrowIfCancellationRequested();

        // ---- Wallpaper --------------------------------------------------
        if (options.ApplyWallpaper && ws.ApplyWallpaper && !string.IsNullOrEmpty(ws.Wallpaper))
        {
            Report(null, DesktopWallpaper.Apply(ws.Wallpaper) ? RestoreStatus.Opened : RestoreStatus.Skipped,
                File.Exists(ws.Wallpaper) ? $"Wallpaper: {Path.GetFileName(ws.Wallpaper)}" : $"Wallpaper missing: {ws.Wallpaper}");
        }

        // ---- Items ------------------------------------------------------
        var profile = DisplayProfiles.Current();
        var profileId = ws.Profile(profile.Id) != null ? profile.Id : ws.Profiles.FirstOrDefault()?.Id;
        var context = new RestoreContext { Workspace = ws, ProfileId = profileId ?? profile.Id };
        var claimed = new HashSet<IntPtr>();
        var placed = new List<(IntPtr Handle, WindowPlacement Placement)>();
        var windowOf = new Dictionary<string, IntPtr>(); // item id -> its window

        var groups = ws.Items
            .Where(i => i.Enabled)
            .GroupBy(i => (Adapter: AdapterRegistry.For(i), Key: AdapterRegistry.For(i).LaunchGroup(i) ?? i.Id))
            .ToList();
        foreach (var item in ws.Items.Where(i => !i.Enabled))
            Report(item.Id, RestoreStatus.Skipped, "not selected");

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            var adapter = group.Key.Adapter;
            var items = group.ToList();
            var live = adapter.LiveWindows();

            // Reuse pass: windows already open that fit an item.
            var pending = new List<WorkspaceItem>();
            foreach (var item in items)
            {
                var wantsExisting = !options.Force && item.Policy != LaunchPolicy.AlwaysNew;
                var hit = wantsExisting ? BestMatch(adapter, item, live, claimed) : null;
                if (hit != null)
                {
                    claimed.Add(hit.Handle);
                    windowOf[item.Id] = hit.Handle;
                    Place(item, hit, context.ProfileId, placed);
                    try { await adapter.ReuseAsync(item, hit, context, ct); }
                    catch (Exception ex) { context.Log?.Invoke($"{item.Name}: {ex.Message}"); }
                    Report(item.Id, RestoreStatus.Opened, "already open; placed");
                }
                else
                    pending.Add(item);
            }
            if (pending.Count == 0) continue;

            foreach (var item in pending) Report(item.Id, RestoreStatus.Launching, "starting…");
            var before = new HashSet<IntPtr>(live.Select(w => w.Handle));
            LaunchResult result;
            try { result = await adapter.LaunchAsync(pending, context, ct); }
            catch (Exception ex) { result = LaunchResult.Failed(ex.Message); }
            if (!result.Started)
            {
                foreach (var item in pending) Report(item.Id, RestoreStatus.Failed, result.Message);
                continue;
            }
            context.Log?.Invoke(result.Message);

            // Wait for the windows and place each as it appears.
            var deadline = DateTime.UtcNow + result.WindowWait;
            var waiting = new List<WorkspaceItem>(pending);
            while (waiting.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(300, ct);
                var now = adapter.LiveWindows();
                var fresh = now.Where(w => !before.Contains(w.Handle) && !claimed.Contains(w.Handle)).ToList();
                foreach (var item in waiting.ToList())
                {
                    var hit = BestMatch(adapter, item, fresh, claimed);
                    if (hit == null) continue;
                    claimed.Add(hit.Handle);
                    windowOf[item.Id] = hit.Handle;
                    fresh.Remove(hit);
                    Place(item, hit, context.ProfileId, placed);
                    Report(item.Id, RestoreStatus.Opened, "opened and placed");
                    waiting.Remove(item);
                }
            }
            foreach (var item in waiting)
                Report(item.Id, RestoreStatus.Opened, "started; window not seen in time, left where the app put it");

            try { await Task.Delay(result.Gap, ct); } catch (TaskCanceledException) { return; }
        }

        // Before the settle pass below, so switching to an open workspace is instant;
        // that pass does not touch the z-order.
        if (options.BringToFront)
            WindowPlacer.BringToFront(ws.Items.Where(i => windowOf.ContainsKey(i.Id)).Select(i => windowOf[i.Id]).ToList());

        // Apps that restore their own window bounds after showing (editors,
        // browsers) can undo a placement made a moment too early; one more pass
        // after everything has settled makes the layout stick.
        if (placed.Count > 0)
        {
            try { await Task.Delay(1200, ct); } catch (TaskCanceledException) { return; }
            foreach (var (handle, placement) in placed) WindowPlacer.Apply(handle, placement);
        }
        Report(null, RestoreStatus.Opened, "Restore finished.");
    }

    static CapturedWindow? BestMatch(AppAdapter adapter, WorkspaceItem item, IReadOnlyList<CapturedWindow> windows, HashSet<IntPtr> claimed)
    {
        CapturedWindow? best = null;
        var bestScore = 0;
        foreach (var w in windows)
        {
            if (claimed.Contains(w.Handle)) continue;
            var score = adapter.Match(item, w);
            if (score > bestScore) { best = w; bestScore = score; }
        }
        return best;
    }

    static void Place(WorkspaceItem item, CapturedWindow window, string profileId, List<(IntPtr, WindowPlacement)> placed)
    {
        var placement = item.PlacementFor(profileId);
        if (placement == null) return;
        if (WindowPlacer.Apply(window.Handle, placement)) placed.Add((window.Handle, placement));
    }
}
