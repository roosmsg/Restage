using Rewake.Models;

namespace Rewake.ViewModels;

/// <summary>One capture in the sidebar history list.</summary>
public class SnapshotItemViewModel : ViewModelBase
{
    readonly SnapshotFile _file;

    public SnapshotItemViewModel(SnapshotFile file) => _file = file;

    public string Path => _file.Path;
    public DateTime Taken => _file.Taken;
    public bool IsLastSession => _file.IsLastSession;
    public int AppCount => _file.AppCount;
    public int FolderCount => _file.FolderCount;

    /// <summary>Primary row text: capture time, or "Last Session" for the pre-reboot pin.</summary>
    public string DisplayLabel => IsLastSession ? "Last Session" : Taken.ToString("HH:mm:ss");

    /// <summary>Secondary row text: relative day plus app/folder counts.</summary>
    public string SubLine
    {
        get
        {
            var datePart = Taken == DateTime.MinValue ? ""
                : Taken.Date == DateTime.Today ? "Today"
                : Taken.ToString("ddd dd MMM");
            var counts = $"{AppCount} apps · {FolderCount} folders";
            if (IsLastSession)
                return Taken == DateTime.MinValue ? counts : $"{Taken:ddd dd MMM, HH:mm} · {counts}";
            return $"{datePart} · {counts}";
        }
    }
}
