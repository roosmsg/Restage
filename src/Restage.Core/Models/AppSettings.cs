namespace Restage.Models;

/// <summary>What to do when a workspace lives on another virtual desktop.</summary>
public enum DesktopSwitchMode
{
    /// <summary>Show a prompt with the desktop's name and wait for the user to switch.</summary>
    Ask = 0,
    // 1 was "wait silently", removed; a settings file holding it falls back to Ask.
    /// <summary>Never wait; restore on whatever desktop is current.</summary>
    Never = 2,
}

/// <summary>User preferences persisted to app-settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"System" (follow Windows), "Dark" or "Light".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Closing the window hides to the tray instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Show Restage's icon in the notification area. Hidden, the app still runs
    /// (hotkeys, local API); starting Restage again opens the window.
    /// </summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>A one-colour tray icon that matches the taskbar's light or dark mode.</summary>
    public bool MonochromeTrayIcon { get; set; }

    public DesktopSwitchMode DesktopSwitch { get; set; } = DesktopSwitchMode.Ask;

    /// <summary>Relaunch apps even when a matching window is already open.</summary>
    public bool ForceLaunch { get; set; }

    /// <summary>Height of the bottom panel when the log is expanded, in pixels.</summary>
    public double BottomPanelHeight { get; set; } = 160;

    /// <summary>True when the activity log is collapsed to just its header.</summary>
    public bool LogCollapsed { get; set; } = true;

    /// <summary>User-given names for display profiles, keyed by profile id.</summary>
    public Dictionary<string, string> ProfileNames { get; set; } = new();

    /// <summary>
    /// Global hotkeys as text ("Ctrl+Alt+R"), keyed by action: see
    /// <see cref="HotkeyActions"/>. Empty by default, so Restage never takes a
    /// key combination the user did not give it. Unknown actions are ignored.
    /// </summary>
    public Dictionary<string, string> Hotkeys { get; set; } = new();
}

/// <summary>
/// The actions a global hotkey can trigger; the keys of <see cref="AppSettings.Hotkeys"/>:
/// start (or switch to) a workspace, "workspace:&lt;id&gt;", and open Restage's window.
/// </summary>
public static class HotkeyActions
{
    const string WorkspacePrefix = "workspace:";

    /// <summary>Show Restage's window.</summary>
    public const string OpenApp = "app:open";

    /// <summary>Start the workspace with this id.</summary>
    public static string Workspace(string id) => WorkspacePrefix + id;

    /// <summary>The workspace id of a <see cref="Workspace"/> action, or null for any other action.</summary>
    public static string? WorkspaceId(string action) =>
        action.StartsWith(WorkspacePrefix, StringComparison.Ordinal) ? action[WorkspacePrefix.Length..] : null;
}
