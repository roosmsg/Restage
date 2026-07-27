namespace Rewake.Models;

/// <summary>Lifecycle of one item (window or folder) during a restore run.</summary>
public enum RestoreStatus
{
    /// <summary>Queued; its turn has not come yet.</summary>
    Waiting,
    /// <summary>Process started; waiting for its window to appear.</summary>
    Launching,
    /// <summary>Launched successfully (terminal state).</summary>
    Opened,
    /// <summary>Not launched — already open, missing, or a duplicate (terminal state).</summary>
    Skipped,
    /// <summary>Launch threw an error; details go to the activity log (terminal state).</summary>
    Failed
}
