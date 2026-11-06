using System.Windows;
using System.Windows.Media;
using Restage.Models;
using Restage.Services;

namespace Restage.ViewModels;

/// <summary>One captured window inside an application group; selectable for restore.</summary>
public class AppItemViewModel : ViewModelBase
{
    /// <summary>The underlying snapshot entry this row represents.</summary>
    public AppEntry Entry { get; }

    public AppItemViewModel(AppEntry entry)
    {
        Entry = entry;
        Icon = IconCache.Get(entry.Exe);
    }

    public string Name => Entry.Name;
    public string Title => string.IsNullOrWhiteSpace(Entry.Title) ? Entry.Exe : Entry.Title;
    public ImageSource? Icon { get; }

    /// <summary>First letter of the app name, shown when no icon could be extracted.</summary>
    public string Initial => string.IsNullOrEmpty(Entry.Name) ? "?" : Entry.Name[..1].ToUpperInvariant();
    public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

    bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
