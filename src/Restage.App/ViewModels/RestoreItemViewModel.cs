using System.Windows;
using System.Windows.Media;
using Restage.Models;

namespace Restage.ViewModels;

/// <summary>One row in the restore progress panel.</summary>
public class RestoreItemViewModel : ViewModelBase
{
    public RestoreItemViewModel(AppItemViewModel app)
    {
        Name = app.Name;
        Sub = app.Entry.Cwds?.FirstOrDefault() ?? app.Title;
        Icon = app.Icon;
        Initial = app.Initial;
    }

    public RestoreItemViewModel(FolderItemViewModel folder)
    {
        Name = folder.Name;
        Sub = folder.Path;
        Icon = folder.Icon;
        Initial = folder.Name is { Length: > 0 } n ? n[..1].ToUpperInvariant() : "?";
    }

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

    /// <summary>True once the item reached a final state (counts toward progress).</summary>
    public bool IsTerminal => Status is RestoreStatus.Opened or RestoreStatus.Skipped or RestoreStatus.Failed;

    public string StatusText => Status switch
    {
        RestoreStatus.Waiting => "Waiting",
        RestoreStatus.Launching => "Launching…",
        RestoreStatus.Opened => "Opened",
        RestoreStatus.Skipped => "Skipped",
        _ => "Failed"
    };
}
