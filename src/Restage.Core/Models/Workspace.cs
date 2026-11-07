using System.Text.Json.Serialization;

namespace Restage.Models;

/// <summary>What kind of thing an item is, which decides the adapter that captures and restores it.</summary>
public enum ItemKind
{
    /// <summary>Any application: launched by exe path and (sanitized) arguments.</summary>
    Generic,
    /// <summary>A terminal window whose shells' working directories are restored as tabs.</summary>
    Terminal,
    /// <summary>A File Explorer window showing <see cref="WorkspaceItem.Target"/>.</summary>
    Explorer,
    /// <summary>A VS Code window on the folder or .code-workspace in <see cref="WorkspaceItem.Target"/>.</summary>
    VSCode,
    /// <summary>An Obsidian window on the vault whose path is <see cref="WorkspaceItem.Target"/>.</summary>
    Obsidian,
    /// <summary>A Firefox browser window of the profile in <see cref="WorkspaceItem.Target"/>.</summary>
    Firefox,
    /// <summary>A Firefox web app pinned to the taskbar; relaunched with its exact arguments.</summary>
    FirefoxTaskbarTab,
    /// <summary>A Chromium browser window (Chrome, Edge, Brave) of the profile in <see cref="WorkspaceItem.Target"/>.</summary>
    Chromium,
    /// <summary>An installed Chromium web app (PWA); <see cref="WorkspaceItem.Target"/> is "profile|app-id".</summary>
    ChromiumApp,
}

/// <summary>How an item is brought back when the workspace is restored.</summary>
public enum LaunchPolicy
{
    /// <summary>Reuse a matching window if one is open, otherwise launch.</summary>
    IfNotRunning,
    /// <summary>Always launch a new instance, even when a matching window is open.</summary>
    AlwaysNew,
    /// <summary>Only focus and place an existing window; never launch.</summary>
    FocusOnly,
    /// <summary>Leave this item alone.</summary>
    Skip,
}

public enum WindowShowState { Normal, Minimized, Maximized }

/// <summary>
/// Where a window sat. For <see cref="WindowShowState.Normal"/> the rectangle is the
/// visible frame (DWM extended frame bounds, screen pixels); for minimized and
/// maximized windows it is the restored rectangle Windows keeps for them
/// (WINDOWPLACEMENT.rcNormalPosition), so a round trip is exact either way.
/// </summary>
public sealed class WindowPlacement
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public WindowShowState State { get; set; } = WindowShowState.Normal;

    /// <summary>Device name of the monitor the window was on (e.g. \\.\DISPLAY1), for diagnostics.</summary>
    public string? Monitor { get; set; }

    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>One thing a workspace brings back: an application window, a folder, a vault…</summary>
public sealed class WorkspaceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public ItemKind Kind { get; set; } = ItemKind.Generic;

    /// <summary>Short display name, usually the process name ("Code", "firefox").</summary>
    public string Name { get; set; } = "";

    /// <summary>Window title at capture time; shown in the UI and used to recognise the window again.</summary>
    public string Title { get; set; } = "";

    /// <summary>What to start: an exe path, an App Execution Alias, or a URI scheme handler's URI.</summary>
    public string Exe { get; set; } = "";

    /// <summary>Arguments to start it with, already sanitized by the adapter.</summary>
    public string Args { get; set; } = "";

    /// <summary>
    /// The item's identity in adapter terms: folder path, vault path, browser
    /// profile, "profile|app-id", or the taskbar tab's id. Null for plain apps.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>Human-readable second line for the UI ("D:\Obsidian\Afstuderen", "Profile: Werk").</summary>
    public string? Detail { get; set; }

    /// <summary>Shell working directories for terminal windows (one per tab).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Cwds { get; set; }

    /// <summary>Unticked items stay in the workspace but are not restored.</summary>
    public bool Enabled { get; set; } = true;

    public LaunchPolicy Policy { get; set; } = LaunchPolicy.IfNotRunning;

    /// <summary>
    /// Open the saved content (the note, folder, file or arguments) when
    /// restoring. Off: just start the app and let it open whatever it opens.
    /// Browsers and web apps ignore this; their profile is the app.
    /// </summary>
    public bool OpenContent { get; set; } = true;

    /// <summary>Window placement per display profile id (see <see cref="DisplayProfile"/>).</summary>
    public Dictionary<string, WindowPlacement> Placements { get; set; } = new();

    /// <summary>Placement for the given profile, else any placement, else null.</summary>
    public WindowPlacement? PlacementFor(string? profileId)
    {
        if (profileId != null && Placements.TryGetValue(profileId, out var p)) return p;
        return Placements.Values.FirstOrDefault();
    }
}

/// <summary>The Windows 11 virtual desktop a workspace lives on.</summary>
public sealed class DesktopRef
{
    public Guid? Id { get; set; }

    /// <summary>Name as shown in Task View; desktops are matched by id first, then by name.</summary>
    public string? Name { get; set; }

    /// <summary>Zero-based position in Task View at capture time.</summary>
    public int? Index { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name! : Index is { } i ? $"Desktop {i + 1}" : "Desktop";
}

/// <summary>One attached monitor, in physical pixels (the process is per-monitor DPI aware).</summary>
public sealed class MonitorInfo
{
    public string Device { get; set; } = "";
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int WorkLeft { get; set; }
    public int WorkTop { get; set; }
    public int WorkWidth { get; set; }
    public int WorkHeight { get; set; }
    public uint Dpi { get; set; } = 96;
    public bool IsPrimary { get; set; }
}

/// <summary>
/// A monitor arrangement. Its id is derived from the devices, their positions,
/// sizes and DPI, so docking or undocking a laptop yields a different profile,
/// and every workspace keeps one set of placements per profile.
/// </summary>
public sealed class DisplayProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<MonitorInfo> Monitors { get; set; } = new();
}

/// <summary>A saved workspace: a desktop, its wallpaper, and the windows to bring back.</summary>
public sealed class Workspace
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>When the workspace was last started; null until the first time.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastOpenedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DesktopRef? Desktop { get; set; }

    /// <summary>Wallpaper image path, applied to the desktop on restore; null leaves it alone.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Wallpaper { get; set; }

    /// <summary>Every display profile this workspace has placements for.</summary>
    public List<DisplayProfile> Profiles { get; set; } = new();

    public List<WorkspaceItem> Items { get; set; } = new();

    /// <summary>Folder the workspace was loaded from; set by the store, never serialized.</summary>
    [JsonIgnore] public string? Directory { get; set; }

    public DisplayProfile? Profile(string? id) => id == null ? null : Profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>Records a profile (replacing an earlier copy with the same id).</summary>
    public void Remember(DisplayProfile profile)
    {
        Profiles.RemoveAll(p => p.Id == profile.Id);
        Profiles.Add(profile);
    }
}
