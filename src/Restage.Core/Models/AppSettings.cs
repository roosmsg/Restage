namespace Restage.Models;

/// <summary>What to do when a workspace lives on another virtual desktop.</summary>
public enum DesktopSwitchMode
{
    /// <summary>Show a prompt with the desktop's name and wait for the user to switch.</summary>
    Ask,
    /// <summary>Wait silently (up to a minute) for the user to switch, then continue.</summary>
    Wait,
    /// <summary>Never wait; restore on whatever desktop is current.</summary>
    Never,
}

/// <summary>User preferences persisted to app-settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Closing the window hides to the tray instead of exiting.</summary>
    public bool MinimizeToTray { get; set; } = true;

    public DesktopSwitchMode DesktopSwitch { get; set; } = DesktopSwitchMode.Ask;

    /// <summary>Apply the workspace's wallpaper when restoring.</summary>
    public bool ApplyWallpaper { get; set; } = true;

    /// <summary>Relaunch apps even when a matching window is already open.</summary>
    public bool ForceLaunch { get; set; }

    /// <summary>Height of the bottom panel when the log is expanded, in pixels.</summary>
    public double BottomPanelHeight { get; set; } = 160;

    /// <summary>True when the activity log is collapsed to just its header.</summary>
    public bool LogCollapsed { get; set; } = true;

    /// <summary>User-given names for display profiles, keyed by profile id.</summary>
    public Dictionary<string, string> ProfileNames { get; set; } = new();
}
