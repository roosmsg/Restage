using System.Collections.ObjectModel;
using System.Windows.Media;
using Restage.Models;
using Restage.Services;

namespace Restage.ViewModels;

/// <summary>One saved workspace: a card in the gallery and, when selected, the details pane.</summary>
public class WorkspaceCardViewModel : ViewModelBase
{
    readonly Action<WorkspaceCardViewModel> _save;

    public WorkspaceCardViewModel(Workspace model, string currentProfileId, Action<WorkspaceCardViewModel> save)
    {
        _save = save;
        Model = model;
        CurrentProfileId = currentProfileId;
        Load();
    }

    public Workspace Model { get; private set; }
    public string CurrentProfileId { get; }

    public string Id => Model.Id;
    public string Name => Model.Name;
    public ImageSource? Thumbnail { get; private set; }
    public bool NoThumbnail => Thumbnail == null;
    public ObservableCollection<WorkspaceItemViewModel> Items { get; } = new();

    /// <summary>The workspace's global hotkey; set by the main view model, which owns the bindings.</summary>
    public HotkeyRowViewModel Hotkey { get; init; } = null!;

    public bool HasDesktop => Model.Desktop != null;
    public string DesktopName => Model.Desktop?.DisplayName ?? "Any desktop";
    public string WallpaperName => string.IsNullOrEmpty(Model.Wallpaper) ? "none"
        : System.IO.Path.GetFileName(Model.Wallpaper) + (Model.ApplyWallpaper ? "" : " (not applied)");
    public bool HasWallpaper => !string.IsNullOrEmpty(Model.Wallpaper);

    /// <summary>Whether Start puts the wallpaper back; set in the edit panel, saved at once.</summary>
    public bool ApplyWallpaper
    {
        get => Model.ApplyWallpaper;
        set
        {
            if (Model.ApplyWallpaper == value) return;
            Model.ApplyWallpaper = value;
            _save(this);
            OnPropertyChanged();
            OnPropertyChanged(nameof(WallpaperName));
        }
    }

    public int EnabledCount => Model.Items.Count(i => i.Enabled);

    /// <summary>Card line one: "Opened 5 minutes ago". The desktop name is already on the preview's badge.</summary>
    public string Subtitle => OpenedText;

    /// <summary>"Opened 5 minutes ago", or "Never opened".</summary>
    public string OpenedText => Model.LastOpenedAt is { } t ? $"Opened {RelativeTime.Ago(t, DateTime.Now)}" : "Never opened";

    /// <summary>"Created today", "Created 7 Nov".</summary>
    public string CreatedText => $"Created {RelativeTime.Date(Model.CreatedAt, DateTime.Now)}";

    /// <summary>Card line two.</summary>
    public string TimesText => CreatedText;

    /// <summary>Re-evaluates the "ago" texts; called after a start and by a minute clock.</summary>
    public void RefreshTimes()
    {
        OnPropertyChanged(nameof(OpenedText));
        OnPropertyChanged(nameof(CreatedText));
        OnPropertyChanged(nameof(TimesText));
        OnPropertyChanged(nameof(Subtitle));
    }

    /// <summary>"Updated today 14:22" style line for the details pane.</summary>
    public string UpdatedText => Model.UpdatedAt.Date == DateTime.Today
        ? $"Updated today {Model.UpdatedAt:HH:mm}"
        : $"Updated {Model.UpdatedAt:ddd dd MMM yyyy, HH:mm}";

    /// <summary>Display profiles this workspace has a layout for; the current one is marked.</summary>
    public string ProfilesText
    {
        get
        {
            if (Model.Profiles.Count == 0) return "no layout saved";
            return string.Join(" · ", Model.Profiles.Select(p => p.Id == CurrentProfileId ? $"{p.Name} (current)" : p.Name));
        }
    }

    /// <summary>True when this workspace has no placements for the monitors attached right now.</summary>
    public bool LacksCurrentProfile => Model.Profiles.Count > 0 && Model.Profile(CurrentProfileId) == null;

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Re-reads everything from the model (after an update or rename).</summary>
    public void Reload(Workspace? model = null)
    {
        if (model != null) Model = model;
        Load();
        foreach (var name in new[]
                 {
                     nameof(Name), nameof(Thumbnail), nameof(NoThumbnail), nameof(Subtitle), nameof(HasDesktop),
                     nameof(DesktopName), nameof(WallpaperName), nameof(HasWallpaper), nameof(ApplyWallpaper), nameof(UpdatedText),
                     nameof(ProfilesText), nameof(LacksCurrentProfile), nameof(EnabledCount),
                     nameof(OpenedText), nameof(CreatedText), nameof(TimesText),
                 })
            OnPropertyChanged(name);
    }

    void Load()
    {
        Thumbnail = IconCache.LoadThumbnail(WorkspaceStore.ThumbnailFor(Model.Id));
        Items.Clear();
        var profileId = Model.Profile(CurrentProfileId) != null ? CurrentProfileId : Model.Profiles.FirstOrDefault()?.Id;
        foreach (var item in Model.Items)
            Items.Add(new WorkspaceItemViewModel(item, profileId, OnItemChanged));
    }

    void OnItemChanged()
    {
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(EnabledCount));
        _save(this);
    }
}
