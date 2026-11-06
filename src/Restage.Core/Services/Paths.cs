namespace Restage.Services;

/// <summary>All file-system locations under %LOCALAPPDATA%\Restage.</summary>
public static class Paths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Restage");

    /// <summary>One folder per workspace: workspace.json plus thumbnail.png.</summary>
    public static readonly string WorkspacesDir = Path.Combine(DataDir, "workspaces");

    public static readonly string SettingsFile = Path.Combine(DataDir, "app-settings.json");
    public static readonly string CrashLog = Path.Combine(DataDir, "crash.log");

    // Still used by the snapshot services inherited from Rewake; they go when
    // the workspace pipeline replaces them.
    public static readonly string SnapDir = Path.Combine(DataDir, "snapshots");
    public static readonly string LastSession = Path.Combine(DataDir, "last-session.json");
    public static readonly string Bootmark = Path.Combine(DataDir, "bootmark.txt");
    public static readonly string CaptureMetaFile = Path.Combine(DataDir, "capture-meta.json");
}
