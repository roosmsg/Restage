using Restage.Models;
using Restage.Services;

namespace Restage.Adapters;

/// <summary>What a capture knows while adapters describe windows.</summary>
public sealed class CaptureContext
{
    public required IReadOnlyList<CapturedWindow> Windows { get; init; }
    public required string ProfileId { get; init; }
}

/// <summary>What a restore knows while adapters launch items.</summary>
public sealed class RestoreContext
{
    public required Workspace Workspace { get; init; }
    public required string ProfileId { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>Outcome of starting an item or a launch group.</summary>
public sealed record LaunchResult(bool Started, string Message)
{
    /// <summary>How long to wait for the window(s) before giving up on placement.</summary>
    public TimeSpan WindowWait { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>Pause after this launch so startup spikes do not pile up.</summary>
    public TimeSpan Gap { get; init; } = TimeSpan.FromMilliseconds(700);

    public static LaunchResult Failed(string message) => new(false, message);
}

/// <summary>
/// One adapter per kind of application. An adapter recognises windows at
/// capture time and turns them into items; at restore time it says which live
/// windows belong to an item, and starts items (singly or as a group) when none
/// do. Adapters never read other processes' memory beyond the shell-provided
/// command line and working directory.
/// </summary>
public abstract class AppAdapter
{
    public abstract ItemKind Kind { get; }

    /// <summary>True when this adapter should describe the window (adapters are tried in registry order).</summary>
    public abstract bool CanDescribe(CapturedWindow window);

    /// <summary>Turns a window into an item. Placement is attached by the capture service.</summary>
    public abstract WorkspaceItem Describe(CapturedWindow window, CaptureContext context);

    /// <summary>
    /// How well a live window fits an item: 0 means not at all; higher is a
    /// better fit. The restore picks the best unclaimed window per item.
    /// </summary>
    public abstract int Match(WorkspaceItem item, CapturedWindow window);

    /// <summary>
    /// Items sharing a non-null key are started by one launch (a browser
    /// profile restores all its windows itself). Null means launch singly.
    /// </summary>
    public virtual string? LaunchGroup(WorkspaceItem item) => null;

    /// <summary>Starts the item(s). Does not wait for windows; the restore service does that.</summary>
    public abstract Task<LaunchResult> LaunchAsync(IReadOnlyList<WorkspaceItem> group, RestoreContext context, CancellationToken ct);

    /// <summary>Live windows that could belong to this adapter's items.</summary>
    public virtual IReadOnlyList<CapturedWindow> LiveWindows() => WindowEnumerator.EnumerateLight();

    /// <summary>Case-insensitive path/name equality helper for adapters.</summary>
    protected static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Splits a window title on " - " and trims the pieces.</summary>
    protected static string[] TitleSegments(string title) =>
        title.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>The adapters in the order they get to claim a window; Generic takes whatever is left.</summary>
public static class AdapterRegistry
{
    public static readonly IReadOnlyList<AppAdapter> All = new AppAdapter[]
    {
        new VSCodeAdapter(),
        new ObsidianAdapter(),
        new FirefoxAdapter(),
        new ChromiumAdapter(),
        new ExplorerAdapter(),
        new GenericAdapter(),
    };

    public static AppAdapter For(CapturedWindow window) => All.First(a => a.CanDescribe(window));

    public static AppAdapter For(ItemKind kind) => All.FirstOrDefault(a => a.Kind == kind)
        ?? All.First(a => a.Kind == ItemKind.Generic);

    /// <summary>Terminal items are a flavour of Generic; everything else maps one-to-one.</summary>
    public static AppAdapter For(WorkspaceItem item) => item.Kind switch
    {
        ItemKind.Terminal => For(ItemKind.Generic),
        ItemKind.FirefoxTaskbarTab => For(ItemKind.Firefox),
        ItemKind.ChromiumApp => For(ItemKind.Chromium),
        var k => For(k),
    };
}
