using System.Text.Json.Serialization;

namespace Rewake.Models;

/// <summary>Saved position and state of one top-level window.</summary>
public class WindowInfo
{
    [JsonPropertyName("left")] public int Left { get; set; }
    [JsonPropertyName("top")] public int Top { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }

    /// <summary>"normal", "minimized" or "maximized".</summary>
    [JsonPropertyName("state")] public string State { get; set; } = "normal";
}

/// <summary>One captured application window and how to launch it again.</summary>
public class AppEntry
{
    /// <summary>Process name without extension (e.g. "msedge").</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>Full path of the executable that owned the window.</summary>
    [JsonPropertyName("exe")] public string Exe { get; set; } = "";

    /// <summary>Command-line arguments the process was started with.</summary>
    [JsonPropertyName("args")] public string Args { get; set; } = "";

    /// <summary>Window title at capture time (shown in the UI).</summary>
    [JsonPropertyName("title")] public string Title { get; set; } = "";

    /// <summary>Shell working directories for terminal windows (one per tab).</summary>
    [JsonPropertyName("cwds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Cwds { get; set; }

    [JsonPropertyName("window")] public WindowInfo? Window { get; set; }
}

/// <summary>A full workspace capture: every open window plus Explorer folders.</summary>
public class Snapshot
{
    /// <summary>Capture time, ISO-8601 round-trip format.</summary>
    [JsonPropertyName("taken")] public string Taken { get; set; } = "";

    /// <summary>Boot identifier the capture was taken in (see SnapshotService.GetBootId).</summary>
    [JsonPropertyName("boot")] public string Boot { get; set; } = "";

    [JsonPropertyName("apps")] public List<AppEntry> Apps { get; set; } = new();
    [JsonPropertyName("folders")] public List<string> Folders { get; set; } = new();
}

/// <summary>Lightweight listing entry for a snapshot file (sidebar rows).</summary>
public record SnapshotFile(string Path, string Label, DateTime Taken, bool IsLastSession, int AppCount,
    int FolderCount, string? Name = null, bool IsFavorite = false)
{
    public string Sub => $"{AppCount} apps · {FolderCount} folders";
}

/// <summary>
/// What the user attached to a capture, stored beside the snapshots rather than
/// inside them (see <see cref="Services.CaptureMetaService"/>).
/// </summary>
public class CaptureMeta
{
    /// <summary>Name shown instead of the capture time; null when unnamed.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>True when the capture is kept: sorted to the top, never pruned.</summary>
    [JsonPropertyName("favorite")] public bool Favorite { get; set; }

    /// <summary>Nothing set means the entry can be dropped on the next write.</summary>
    [JsonIgnore] public bool IsEmpty => !Favorite && string.IsNullOrWhiteSpace(Name);
}

/// <summary>User preferences persisted to app-settings.json.</summary>
public class AppSettings
{
    public bool AutoCapture { get; set; } = true;
    public int IntervalMinutes { get; set; } = 2;
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Width of the capture-history sidebar, in pixels (drag to resize).</summary>
    public double SidebarWidth { get; set; } = 272;

    /// <summary>Height of the bottom panel when the log is expanded, in pixels.</summary>
    public double BottomPanelHeight { get; set; } = 181;

    /// <summary>True when the activity log is collapsed to just its header.</summary>
    public bool LogCollapsed { get; set; }

    /// <summary>True when the capture-history sidebar is hidden.</summary>
    public bool SidebarCollapsed { get; set; }

    // ---- Stay Awake (Services.StayAwakeService) ----
    // Only the configuration is saved; whether it is running is not. A machine
    // that reboots should come back asleep-able until the user says otherwise.

    /// <summary>Seconds between simulated-activity nudges.</summary>
    public int StayAwakeIntervalSeconds { get; set; } = 60;

    /// <summary>"Invisible" (1px move &amp; return), "Visible" (small drift) or "Key" (F15 tap).</summary>
    public string StayAwakeMethod { get; set; } = "Visible";

    /// <summary>Skip the nudge whenever the user has produced real input within the interval.</summary>
    public bool StayAwakeOnlyWhenIdle { get; set; } = true;

    /// <summary>True to stop automatically after <see cref="StayAwakeAutoStopHours"/>.</summary>
    public bool StayAwakeAutoStop { get; set; }

    /// <summary>Hours before an automatic stop (guards a forgotten Friday session).</summary>
    public int StayAwakeAutoStopHours { get; set; } = 2;
}
