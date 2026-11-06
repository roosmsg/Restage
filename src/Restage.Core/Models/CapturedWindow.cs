namespace Restage.Models;

/// <summary>A visible top-level window as seen at capture or restore time.</summary>
public sealed class CapturedWindow
{
    public IntPtr Handle { get; init; }
    public int Pid { get; init; }
    public string Title { get; init; } = "";
    public string Class { get; init; } = "";

    /// <summary>Process name without extension ("Code", "firefox").</summary>
    public string ProcessName { get; init; } = "";

    /// <summary>Full path of the executable; empty when unreadable (elevated process).</summary>
    public string ExePath { get; init; } = "";

    /// <summary>Raw command line of the owning process, when available.</summary>
    public string CommandLine { get; init; } = "";

    /// <summary>Command line without its executable token.</summary>
    public string Args { get; init; } = "";

    /// <summary>The window's AppUserModelID, when the app set one (Chromium PWAs, Store apps).</summary>
    public string? AppUserModelId { get; init; }

    public WindowPlacement Placement { get; init; } = new();

    /// <summary>Shell working directories behind a terminal window (one per tab), else null.</summary>
    public List<string>? ShellCwds { get; init; }

    public string ExeName => System.IO.Path.GetFileName(ExePath);
}
