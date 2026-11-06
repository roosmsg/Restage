using System.IO;

namespace Restage.Services;

/// <summary>All file-system locations under %LOCALAPPDATA%\Restage.</summary>
public static class Paths
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Restage");
    public static readonly string SnapDir = Path.Combine(DataDir, "snapshots");
    public static readonly string LastSession = Path.Combine(DataDir, "last-session.json");
    public static readonly string Bootmark = Path.Combine(DataDir, "bootmark.txt");
    public static readonly string SettingsFile = Path.Combine(DataDir, "app-settings.json");
    public static readonly string CaptureMetaFile = Path.Combine(DataDir, "capture-meta.json");
    public static readonly string CrashLog = Path.Combine(DataDir, "crash.log");

    // One-time data migration from the tool's previous names
    // (WorkspaceRestore → StateKeeper → Restage).
    static Paths()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var oldName in new[] { "StateKeeper", "WorkspaceRestore" })
            {
                var oldDir = Path.Combine(local, oldName);
                if (Directory.Exists(oldDir) && !Directory.Exists(DataDir))
                {
                    Directory.Move(oldDir, DataDir);
                    break;
                }
            }
        }
        catch { }
    }
}
