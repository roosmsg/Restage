using Rewake.Models;

namespace Rewake.ViewModels;

/// <summary>One capture in the sidebar history list.</summary>
public class SnapshotItemViewModel : ViewModelBase
{
    readonly SnapshotFile _file;

    public SnapshotItemViewModel(SnapshotFile file, MainViewModel owner)
    {
        _file = file;
        // The row's own commands, because its context menu lives outside the
        // visual tree: a menu item reaching up for the window's view model needs
        // PlacementTarget gymnastics, and would then act on whichever row is
        // *selected* rather than the one that was right-clicked.
        ToggleFavoriteCommand = new RelayCommand(_ => owner.ToggleFavorite(this), _ => CanTag);
        RenameCommand = new RelayCommand(_ => owner.AskRename(this), _ => CanTag);
        DeleteCommand = new RelayCommand(_ => owner.DeleteCapture(this));
    }

    public string Path => _file.Path;
    public DateTime Taken => _file.Taken;
    public bool IsLastSession => _file.IsLastSession;
    public int AppCount => _file.AppCount;
    public int FolderCount => _file.FolderCount;

    /// <summary>User-given name, or null when the row still shows its time.</summary>
    public string? Name => _file.Name;

    public bool IsFavorite => _file.IsFavorite;

    public RelayCommand ToggleFavoriteCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }

    /// <summary>
    /// The pinned row can't be named or starred: File.Copy overwrites
    /// last-session.json on the first capture after every boot, so anything
    /// attached to it would quietly end up describing a different capture.
    /// </summary>
    public bool CanTag => !IsLastSession;

    public string FavoriteLabel => IsFavorite ? "Remove from favourites" : "Add to favourites";

    /// <summary>Primary row text: the name if it has one, else the capture time.</summary>
    public string DisplayLabel => IsLastSession ? "Last Session"
        : string.IsNullOrWhiteSpace(Name) ? Taken.ToString("HH:mm:ss")
        : Name!;

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
            // A named row gave its time slot away, so the time comes back here —
            // "the 14:22 one" is still how you tell two captures apart.
            if (!string.IsNullOrWhiteSpace(Name) && Taken != DateTime.MinValue)
                return $"{datePart} {Taken:HH:mm:ss} · {counts}";
            return $"{datePart} · {counts}";
        }
    }
}
