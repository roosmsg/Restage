using System.Windows;
using System.Windows.Media;
using Restage.Models;
using Restage.Services;

namespace Restage.ViewModels;

/// <summary>One row in the restore progress panel.</summary>
public class RestoreRowViewModel : ViewModelBase
{
    public RestoreRowViewModel(WorkspaceItem item)
    {
        ItemId = item.Id;
        Name = WorkspaceItemViewModel.Label(item);
        Sub = item.Detail ?? item.Title;
        Icon = IconCache.ForItem(item);
        Initial = Name is { Length: > 0 } n ? n[..1].ToUpperInvariant() : "?";
    }

    public string ItemId { get; }
    public string Name { get; }
    public string Sub { get; }
    public ImageSource? Icon { get; }
    public string Initial { get; }
    public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

    RestoreStatus _status = RestoreStatus.Waiting;
    public RestoreStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsTerminal));
            }
        }
    }

    string _message = "";
    /// <summary>What the restore service last said about this item.</summary>
    public string Message
    {
        get => _message;
        set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(StatusText)); }
    }

    /// <summary>True once the item reached a final state (counts toward progress).</summary>
    public bool IsTerminal => Status is RestoreStatus.Opened or RestoreStatus.Skipped or RestoreStatus.Failed;

    public string StatusText => Status switch
    {
        RestoreStatus.Waiting => "Waiting",
        RestoreStatus.Launching => "Starting…",
        RestoreStatus.Opened => Message.Length > 0 ? Message : "Opened",
        RestoreStatus.Skipped => Message.Length > 0 ? $"Skipped · {Message}" : "Skipped",
        _ => Message.Length > 0 ? $"Failed · {Message}" : "Failed",
    };
}
