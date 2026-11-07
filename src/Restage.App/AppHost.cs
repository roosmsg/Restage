namespace Restage;

/// <summary>What the view model needs from the application shell.</summary>
public interface IAppHost
{
    /// <summary>Shows (creating if needed) and activates the main window.</summary>
    void ShowMain();

    /// <summary>Hides the main window, e.g. while the desktop is photographed.</summary>
    void HideMain();

    /// <summary>Native handle of the main window, or zero when it does not exist.</summary>
    IntPtr MainHandle { get; }

    /// <summary>True while the main window is on screen (not hidden to the tray).</summary>
    bool IsMainVisible { get; }

    /// <summary>Shows, hides or repaints the tray icon after its settings changed.</summary>
    void RefreshTray();

    /// <summary>Removes the tray icon and exits for real.</summary>
    void ExitApp();
}
