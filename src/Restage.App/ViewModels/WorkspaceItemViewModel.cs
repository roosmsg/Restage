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

    /// <summary>
    /// Whether the item has content to open beyond the app itself (a note,
    /// folder, file…). A plain app only counts when its arguments carry
    /// something to open; switches alone ("--updated") are launch details, not
    /// something a user would want to tick on or off.
    /// </summary>
    public bool HasContent => Item.Kind switch
    {
        ItemKind.Obsidian => Item.Args.Contains("&file=", StringComparison.Ordinal),
        ItemKind.VSCode => Item.Target != null,
        ItemKind.Explorer => true,
        ItemKind.Terminal => Item.Cwds is { Count: > 0 } || LaunchHelpers.HasContentArgs(Item.Args),
        ItemKind.Generic => LaunchHelpers.HasContentArgs(Item.Args),
        _ => false,
    };

    /// <summary>What the content checkbox opens: "note", "folder", "folders", or "file" for a plain app's path or URL.</summary>
    public string ContentLabel => Item.Kind switch
    {
        ItemKind.Obsidian => "note",
        ItemKind.VSCode or ItemKind.Explorer => "folder",
        ItemKind.Terminal when Item.Cwds is { Count: > 0 } => "folders",
        _ => "file",
    };

    public bool OpenContent
    {
        get => Item.OpenContent;
        set
        {
            if (Item.OpenContent == value) return;
            Item.OpenContent = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string PolicyLabel => PolicyText(Item.Policy);

    /// <summary>Switches between reuse if open and always launch; one themed button instead of a combo box.</summary>
    public void NextPolicy()
    {
        Item.Policy = Item.Policy == LaunchPolicy.AlwaysNew ? LaunchPolicy.IfNotRunning : LaunchPolicy.AlwaysNew;
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

    public static string PolicyText(LaunchPolicy policy) =>
        policy == LaunchPolicy.AlwaysNew ? "always launch" : "reuse if open";
}
