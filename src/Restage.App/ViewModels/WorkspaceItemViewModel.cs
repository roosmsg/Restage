using System.Windows;
using System.Windows.Media;
using Restage.Models;
using Restage.Services;

namespace Restage.ViewModels;

/// <summary>One item of the selected workspace, editable in the details pane.</summary>
public class WorkspaceItemViewModel : ViewModelBase
{
    readonly Action _changed;

    public WorkspaceItemViewModel(WorkspaceItem item, string? profileId, Action changed)
    {
        Item = item;
        _changed = changed;
        Icon = IconCache.ForItem(item);
        var p = item.PlacementFor(profileId);
        PlacementText = p == null ? "no saved position"
            : $"{p.State.ToString().ToLowerInvariant()} · {p.Left},{p.Top} · {p.Width}×{p.Height}";
    }

    public WorkspaceItem Item { get; }

    public string Name => Label(Item);
    public string Detail => Item.Detail ?? Item.Title;
    public string Title => Item.Title;
    public string KindLabel => KindText(Item.Kind);
    public string PlacementText { get; }
    public ImageSource? Icon { get; }
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
    public Visibility FallbackVisibility => Icon == null ? Visibility.Visible : Visibility.Collapsed;

    public bool Enabled
    {
        get => Item.Enabled;
        set
        {
            if (Item.Enabled == value) return;
            Item.Enabled = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string PolicyLabel => PolicyText(Item.Policy);

    /// <summary>Cycles through the launch policies; one themed button instead of a combo box.</summary>
    public void NextPolicy()
    {
        Item.Policy = Item.Policy switch
        {
            LaunchPolicy.IfNotRunning => LaunchPolicy.AlwaysNew,
            LaunchPolicy.AlwaysNew => LaunchPolicy.FocusOnly,
            LaunchPolicy.FocusOnly => LaunchPolicy.Skip,
            _ => LaunchPolicy.IfNotRunning,
        };
        OnPropertyChanged(nameof(PolicyLabel));
        _changed();
    }

    public static string Label(WorkspaceItem item) => item.Kind switch
    {
        ItemKind.VSCode => "VS Code",
        ItemKind.Obsidian => "Obsidian",
        ItemKind.Explorer => "Explorer",
        ItemKind.Firefox => "Firefox",
        ItemKind.FirefoxTaskbarTab => "Firefox web app",
        ItemKind.Chromium => Browser(item.Name),
        ItemKind.ChromiumApp => $"{Browser(item.Name.Replace(" app", ""))} web app",
        _ => item.Name,
    };

    static string Browser(string process) => process.ToLowerInvariant() switch
    {
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "brave" => "Brave",
        "vivaldi" => "Vivaldi",
        var p => p,
    };

    public static string KindText(ItemKind kind) => kind switch
    {
        ItemKind.VSCode => "folder",
        ItemKind.Obsidian => "vault",
        ItemKind.Explorer => "folder",
        ItemKind.Firefox or ItemKind.Chromium => "profile",
        ItemKind.FirefoxTaskbarTab or ItemKind.ChromiumApp => "web app",
        ItemKind.Terminal => "terminal",
        _ => "app",
    };

    public static string PolicyText(LaunchPolicy policy) => policy switch
    {
        LaunchPolicy.IfNotRunning => "reuse if open",
        LaunchPolicy.AlwaysNew => "always launch",
        LaunchPolicy.FocusOnly => "only if open",
        _ => "skip",
    };
}
