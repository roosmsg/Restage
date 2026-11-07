using System.Windows;
using System.Windows.Media;
using Restage.Models;
using Restage.Services;

namespace Restage.ViewModels;

/// <summary>A window found by a capture, waiting for the user to keep or drop it.</summary>
public class CaptureItemViewModel : ViewModelBase
{
    public CaptureItemViewModel(WorkspaceItem item)
    {
        Item = item;
        Icon = IconCache.ForItem(item);
    }

    public WorkspaceItem Item { get; }
    public string Name => WorkspaceItemViewModel.Label(Item);
    public string Detail => Item.Detail ?? Item.Title;
    public string KindLabel => WorkspaceItemViewModel.KindText(Item.Kind);
    public ImageSource? Icon { get; }
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

    bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
