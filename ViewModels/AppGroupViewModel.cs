using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Restage.Models;
using Restage.Services;

namespace Restage.ViewModels;

/// <summary>All captured windows of one application, shown as an expandable card.</summary>
public class AppGroupViewModel : ViewModelBase
{
    // Browsers restore their own windows via their session-restore feature, so
    // launching one instance is usually right — default-select only the first.
    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "firefox", "opera", "vivaldi"
    };

    public AppGroupViewModel(IReadOnlyList<AppEntry> entries)
    {
        Name = entries[0].Name;
        Exe = entries[0].Exe;
        Icon = IconCache.Get(Exe);

        var isBrowser = Browsers.Contains(Name);
        for (var i = 0; i < entries.Count; i++)
        {
            var vm = new AppItemViewModel(entries[i]);
            if (isBrowser && i > 0) vm.IsSelected = false;
            vm.PropertyChanged += InstanceChanged;
            Instances.Add(vm);
        }
    }

    /// <summary>Raised when any instance's selection changes (for the master checkbox).</summary>
    public event Action? SelectionChanged;

    public string Name { get; }
    public string Exe { get; }
    public ImageSource? Icon { get; }

    /// <summary>First letter of the app name, shown when no icon could be extracted.</summary>
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<AppItemViewModel> Instances { get; } = new();

    public int InstanceCount => Instances.Count;
    public bool HasMultiple => Instances.Count > 1;
    public Visibility BadgeVisibility => HasMultiple ? Visibility.Visible : Visibility.Collapsed;
    public string CountLabel => $"{InstanceCount} windows";
    public string Subtitle => HasMultiple ? $"{InstanceCount} windows open" : Instances[0].Title;

    bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    /// <summary>Tri-state: true = all windows, false = none, null = some.</summary>
    public bool? GroupSelected
    {
        get
        {
            var selected = Instances.Count(i => i.IsSelected);
            if (selected == 0) return false;
            if (selected == Instances.Count) return true;
            return null;
        }
        set
        {
            var on = value == true;
            foreach (var i in Instances) i.IsSelected = on;
            OnPropertyChanged();
        }
    }

    void InstanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(GroupSelected));
            SelectionChanged?.Invoke();
        }
    }
}
