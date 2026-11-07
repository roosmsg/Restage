using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>
/// File Explorer folder windows. They are not taken from the window list (the
/// capture service asks the shell for them, path and handle included) but they
/// are launched, matched and placed like any other item.
/// </summary>
public sealed class ExplorerAdapter : AppAdapter
{
    public override ItemKind Kind => ItemKind.Explorer;

    static string ExplorerExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    // Explorer windows are excluded from the generic enumeration; see WindowEnumerator.
    public override bool CanDescribe(CapturedWindow window) => false;

    public override WorkspaceItem Describe(CapturedWindow window, CaptureContext context) =>
        FromFolder(window.Title);

    public static WorkspaceItem FromFolder(string path) => new()
    {
        Kind = ItemKind.Explorer,
        Name = "Explorer",
        Title = Path.GetFileName(ExplorerWindows.Normalize(path)) is { Length: > 0 } n ? n : path,
        Exe = ExplorerExe,
        Args = $"\"{path}\"",
        Target = path,
        Detail = path,
    };

    public override int Match(WorkspaceItem item, CapturedWindow window) =>
        item.Target != null && Same(ExplorerWindows.Normalize(item.Target), ExplorerWindows.Normalize(window.Title)) ? 3 : 0;

    /// <summary>Open folders as pseudo-windows: the path is the title, the handle is the Explorer window.</summary>
    public override IReadOnlyList<CapturedWindow> LiveWindows() =>
        ExplorerWindows.ListSta().Select(e => new CapturedWindow
        {
            Handle = e.Handle,
            Title = e.Path,
            ProcessName = "explorer",
            ExePath = ExplorerExe,
            Placement = WindowPlacer.Capture(e.Handle),
        }).ToList();

    public override Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct)
    {
        var item = group[0];
        if (item.Target == null || !Directory.Exists(item.Target))
            return Task.FromResult(LaunchResult.Failed($"folder missing: {item.Target}"));
        try
        {
            LaunchHelpers.Start("explorer.exe", $"\"{item.Target}\"");
            return Task.FromResult(new LaunchResult(true, $"opened {item.Target}")
            {
                Gap = TimeSpan.FromMilliseconds(300),
            });
        }
        catch (Exception ex) { return Task.FromResult(LaunchResult.Failed(ex.Message)); }
    }
}
