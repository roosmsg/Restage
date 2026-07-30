using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Rewake.Models;
using Rewake.Services;

namespace Rewake.ViewModels;

/// <summary>
/// Root view model for the main window: capture history, the loaded capture's
/// app/folder selection, restore progress, settings toggles, and the activity log.
/// </summary>
public class MainViewModel : ViewModelBase
{
    readonly AppSettings _settings;
    string? _loadedPath;              // snapshot file currently shown in the lists
    DateTime _loadedWriteTime;        // its write time, to detect rewritten content
    bool _refreshing;                 // guards selection while the sidebar rebuilds

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        RestoreCommand = new RelayCommand(async _ => await RestoreAsync(), _ => !IsRestoring);
        CaptureNowCommand = new RelayCommand(async _ => await CaptureNowAsync());
        DeleteCaptureCommand = new RelayCommand(_ => DeleteCapture(SelectedSnapshot),
            _ => SelectedSnapshot != null);
        FavoriteSelectedCommand = new RelayCommand(_ => ToggleFavorite(SelectedSnapshot!),
            _ => SelectedSnapshot?.CanTag == true);
        OpenDataFolderCommand = new RelayCommand(_ => OpenDataFolder());
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        // Clearing the log throws away a few screens of text; clearing the
        // history deletes files. Only the second one asks.
        ClearLogCommand = new RelayCommand(_ => ClearLog(), _ => LogEntries.Count > 0);
        ClearHistoryCommand = new RelayCommand(_ => AskClearHistory(), _ => HasClearableHistory);
        ConfirmAcceptCommand = new RelayCommand(_ => AcceptConfirm());
        ConfirmCancelCommand = new RelayCommand(_ => CloseConfirm());
        RefreshSnapshots();
        Log("Ready. Closing the window keeps Rewake running in the system tray.");
    }

    public ObservableCollection<SnapshotItemViewModel> Snapshots { get; } = new();
    public ObservableCollection<AppGroupViewModel> Apps { get; } = new();
    public ObservableCollection<FolderItemViewModel> Folders { get; } = new();

    IEnumerable<AppItemViewModel> AllInstances => Apps.SelectMany(g => g.Instances);
    public ObservableCollection<string> LogEntries { get; } = new();

    /// <summary>Rows of the restore overlay, one per selected window/folder.</summary>
    public ObservableCollection<RestoreItemViewModel> RestoreProgress { get; } = new();

    public RelayCommand RestoreCommand { get; }
    public RelayCommand CaptureNowCommand { get; }
    public RelayCommand DeleteCaptureCommand { get; }
    public RelayCommand FavoriteSelectedCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public RelayCommand ConfirmAcceptCommand { get; }
    public RelayCommand ConfirmCancelCommand { get; }

    /// <summary>Raised when the auto-capture setting changes so the host can reconfigure its timer.</summary>
    public event Action? AutoCaptureChanged;

    SnapshotItemViewModel? _selectedSnapshot;
    public SnapshotItemViewModel? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            // The ListBox pushes a null selection while the list is being
            // rebuilt — ignoring it preserves the user's checkbox choices.
            if (_refreshing && value == null) return;
            if (SetProperty(ref _selectedSnapshot, value) && NeedsReload(value))
                LoadSnapshot(value);
            DeleteCaptureCommand.RaiseCanExecuteChanged();
            NotifyFavoriteChanged();
        }
    }

    /// <summary>Fills the star in the action row for the selected capture.</summary>
    public bool SelectedIsFavorite => SelectedSnapshot?.IsFavorite == true;

    public string FavoriteTooltip => SelectedIsFavorite ? "Remove from favourites" : "Add to favourites";

    void NotifyFavoriteChanged()
    {
        OnPropertyChanged(nameof(SelectedIsFavorite));
        OnPropertyChanged(nameof(FavoriteTooltip));
        FavoriteSelectedCommand.RaiseCanExecuteChanged();
    }

    bool NeedsReload(SnapshotItemViewModel? item)
    {
        if (item?.Path != _loadedPath) return true;
        if (item == null) return false;
        // Same path but rewritten content (last-session.json after a reboot)
        try { return File.GetLastWriteTime(item.Path) != _loadedWriteTime; }
        catch { return true; }
    }

    string _header = "No capture selected";
    public string Header { get => _header; private set => SetProperty(ref _header, value); }

    string _subHeader = "";
    public string SubHeader { get => _subHeader; private set => SetProperty(ref _subHeader, value); }

    string _captureStats = "";
    public string CaptureStats { get => _captureStats; private set => SetProperty(ref _captureStats, value); }

    public string HistoryCount => Snapshots.Count.ToString();
    public string AppCount => Apps.Count.ToString();
    public string FolderCount => Folders.Count.ToString();

    bool _isRestoring;
    public bool IsRestoring
    {
        get => _isRestoring;
        private set
        {
            if (SetProperty(ref _isRestoring, value)) RestoreCommand.RaiseCanExecuteChanged();
        }
    }

    bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        private set => SetProperty(ref _isCapturing, value);
    }

    bool _showRestorePanel;
    /// <summary>Restore overlay visibility: shown while restoring, lingers a few
    /// seconds with the summary, then hidden (XAML fades on the falling edge).</summary>
    public bool ShowRestorePanel
    {
        get => _showRestorePanel;
        private set => SetProperty(ref _showRestorePanel, value);
    }

    int _restoreDone, _restoreTotal;

    /// <summary>Overlay counter text, e.g. "2 of 6".</summary>
    public string RestoreCounter => $"{_restoreDone} of {_restoreTotal}";

    /// <summary>Overall restore progress in [0, 1] for the progress bar.</summary>
    public double RestoreFraction => _restoreTotal == 0 ? 0 : (double)_restoreDone / _restoreTotal;

    /// <summary>Tri-state master checkbox: true = all, false = none, null = mixed.</summary>
    public bool? AllSelected
    {
        get
        {
            var total = AllInstances.Count() + Folders.Count;
            if (total == 0) return false;
            var selected = AllInstances.Count(a => a.IsSelected) + Folders.Count(f => f.IsSelected);
            if (selected == 0) return false;
            if (selected == total) return true;
            return null;
        }
        set
        {
            var on = value == true;
            foreach (var a in AllInstances) a.IsSelected = on;
            foreach (var f in Folders) f.IsSelected = on;
            OnPropertyChanged();
        }
    }

    public bool AutoCapture
    {
        get => _settings.AutoCapture;
        set
        {
            if (_settings.AutoCapture == value) return;
            _settings.AutoCapture = value;
            SettingsService.Save(_settings);
            AutoCaptureChanged?.Invoke();
            OnPropertyChanged();
            Log(value ? "Auto capture enabled." : "Auto capture disabled.");
        }
    }

    public bool StartWithWindows
    {
        get => SettingsService.GetStartWithWindows();
        set
        {
            try
            {
                SettingsService.SetStartWithWindows(value);
                Log(value
                    ? "Rewake will run at startup (minimized to tray)."
                    : "Removed from startup.");
            }
            catch (Exception ex) { Log($"Startup setting failed: {ex.Message}"); }
            OnPropertyChanged();
        }
    }

    bool _forceLaunch;
    public bool ForceLaunch { get => _forceLaunch; set => SetProperty(ref _forceLaunch, value); }

    public bool IsDarkTheme => !string.Equals(_settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);

    /// <summary>Segoe MDL2 glyph for the theme toggle: sun in dark mode, moon in light.</summary>
    public string ThemeGlyph => IsDarkTheme ? "" : "";
    public string ThemeTooltip => IsDarkTheme ? "Switch to light mode" : "Switch to dark mode";

    void ToggleTheme()
    {
        _settings.Theme = IsDarkTheme ? "Light" : "Dark";
        SettingsService.Save(_settings);
        ThemeService.Apply(_settings.Theme);
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeGlyph));
        OnPropertyChanged(nameof(ThemeTooltip));
        Log($"Switched to {_settings.Theme.ToLowerInvariant()} mode.");
    }

    /// <summary>Rebuilds the sidebar list, preserving the current selection when possible.</summary>
    public void RefreshSnapshots()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) { dispatcher.Invoke(RefreshSnapshots); return; }

        var selectedPath = SelectedSnapshot?.Path;
        _refreshing = true;
        try
        {
            Snapshots.Clear();
            foreach (var sf in SnapshotService.ListSnapshots())
                Snapshots.Add(new SnapshotItemViewModel(sf, this));
        }
        finally { _refreshing = false; }
        SelectedSnapshot =
            Snapshots.FirstOrDefault(s => s.Path == selectedPath) ?? Snapshots.FirstOrDefault();
        ClearHistoryCommand.RaiseCanExecuteChanged();
        // Rows are rebuilt rather than mutated, so the selected row is a new
        // object even when it is the same capture — the star has to be re-asked.
        NotifyFavoriteChanged();

        try
        {
            long bytes = 0;
            if (Directory.Exists(Paths.SnapDir))
                bytes += new DirectoryInfo(Paths.SnapDir).GetFiles("snap-*.json").Sum(f => f.Length);
            if (File.Exists(Paths.LastSession)) bytes += new FileInfo(Paths.LastSession).Length;
            CaptureStats = $"{Snapshots.Count} captures · {bytes / 1024:N0} KB on disk";
        }
        catch { CaptureStats = $"{Snapshots.Count} captures"; }

        OnPropertyChanged(nameof(HistoryCount));
    }

    /// <summary>Loads a capture's apps and folders into the selection lists.</summary>
    void LoadSnapshot(SnapshotItemViewModel? item)
    {
        if (!IsRestoring) ShowRestorePanel = false;
        Apps.Clear();
        Folders.Clear();
        _loadedPath = null;
        if (item == null)
        {
            Header = "No capture selected";
            SubHeader = "";
            NotifyListsChanged();
            return;
        }
        var snap = SnapshotService.Load(item.Path);
        if (snap == null) { NotifyListsChanged(); return; }

        foreach (var group in snap.Apps
                     .Where(a => !string.IsNullOrEmpty(a.Exe))
                     .GroupBy(a => a.Exe, StringComparer.OrdinalIgnoreCase))
        {
            var vm = new AppGroupViewModel(group.ToList());
            vm.SelectionChanged += () => OnPropertyChanged(nameof(AllSelected));
            Apps.Add(vm);
        }
        foreach (var f in snap.Folders)
        {
            var vm = new FolderItemViewModel(f);
            vm.PropertyChanged += ItemSelectionChanged;
            Folders.Add(vm);
        }

        var windowCount = snap.Apps.Count;
        Header = item.IsLastSession ? "Last Session" : "Capture";
        SubHeader = item.IsLastSession
            ? $"Captured {item.Taken:ddd dd MMM yyyy 'at' HH:mm} — everything open before shutdown"
            : $"Captured {item.Taken:ddd dd MMM yyyy, HH:mm:ss} · {Apps.Count} applications · {windowCount} windows · {snap.Folders.Count} folders";

        _loadedPath = item.Path;
        try { _loadedWriteTime = File.GetLastWriteTime(item.Path); } catch { }
        NotifyListsChanged();
    }

    void ItemSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppItemViewModel.IsSelected))
            OnPropertyChanged(nameof(AllSelected));
    }

    void NotifyListsChanged()
    {
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(AppCount));
        OnPropertyChanged(nameof(FolderCount));
    }

    async Task RestoreAsync()
    {
        var selectedApps = AllInstances.Where(a => a.IsSelected).ToList();
        var selectedFolders = Folders.Where(f => f.IsSelected).ToList();
        var apps = selectedApps.Select(a => a.Entry).ToList();
        var folders = selectedFolders.Select(f => f.Path).ToList();
        if (apps.Count == 0 && folders.Count == 0) { Log("Nothing selected to restore."); return; }

        RestoreProgress.Clear();
        foreach (var a in selectedApps) RestoreProgress.Add(new RestoreItemViewModel(a));
        foreach (var f in selectedFolders) RestoreProgress.Add(new RestoreItemViewModel(f));
        _restoreDone = 0;
        _restoreTotal = RestoreProgress.Count;
        OnPropertyChanged(nameof(RestoreCounter));
        OnPropertyChanged(nameof(RestoreFraction));
        ShowRestorePanel = true;

        IsRestoring = true;
        Log($"Restoring workspace — {apps.Count} windows, {folders.Count} folders…");
        try
        {
            await Task.Run(() => RestoreService.RestoreAsync(apps, folders, ForceLaunch, Log, OnRestoreProgress));
        }
        finally { IsRestoring = false; }

        // Let the summary linger, then fade the overlay away (the XAML side
        // animates opacity on ShowRestorePanel's falling edge).
        await Task.Delay(4000);
        if (!IsRestoring) ShowRestorePanel = false;
    }

    /// <summary>
    /// Restore-thread callback: updates the matching overlay row (marshalled to
    /// the UI thread) and advances the counter when a row reaches a final state.
    /// </summary>
    void OnRestoreProgress(int index, RestoreStatus status)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => OnRestoreProgress(index, status));
            return;
        }
        if (index < 0 || index >= RestoreProgress.Count) return;
        var item = RestoreProgress[index];
        var wasTerminal = item.IsTerminal;
        item.Status = status;
        if (!wasTerminal && item.IsTerminal)
        {
            _restoreDone++;
            OnPropertyChanged(nameof(RestoreCounter));
            OnPropertyChanged(nameof(RestoreFraction));
        }
    }

    /// <summary>Manual capture from the header button or tray menu.</summary>
    public async Task CaptureNowAsync()
    {
        if (IsCapturing) return;
        IsCapturing = true;
        Log("Capturing workspace…");
        try
        {
            await Task.Run(() => SnapshotService.SaveSnapshot(SnapshotService.CaptureSta()));
            RefreshSnapshots();
            Log("Capture saved.");
        }
        finally { IsCapturing = false; }
    }

    /// <summary>Timer capture: no log chatter, just refresh the list.</summary>
    public async Task CaptureSilentAsync()
    {
        await Task.Run(() => SnapshotService.SaveSnapshot(SnapshotService.CaptureSta()));
        RefreshSnapshots();
    }

    /// <summary>
    /// Deletes one capture, from either the toolbar (the selected row) or a row's
    /// own context menu. No confirmation in either case: it is the same action
    /// reached two ways, and one entry point must not behave differently from
    /// the other.
    /// </summary>
    public void DeleteCapture(SnapshotItemViewModel? s)
    {
        if (s == null) return;
        try
        {
            File.Delete(s.Path);
            CaptureMetaService.Forget(s.Path);
            Log($"Deleted capture: {s.DisplayLabel} ({s.SubLine}).");
        }
        catch (Exception ex) { Log($"Delete failed: {ex.Message}"); }
        _loadedPath = null;
        RefreshSnapshots();
    }

    /// <summary>
    /// Stars or unstars a capture. Favourites sort to the top of the sidebar and
    /// survive both the 30-capture prune and Clear History, so the list has to be
    /// rebuilt rather than just repainted.
    /// </summary>
    public void ToggleFavorite(SnapshotItemViewModel item)
    {
        if (!item.CanTag) return;
        var favorite = !item.IsFavorite;
        CaptureMetaService.SetFavorite(item.Path, favorite);
        Log(favorite
            ? $"Added to favourites: {item.DisplayLabel}. Favourites are never pruned."
            : $"Removed from favourites: {item.DisplayLabel}.");
        RefreshSnapshots();
    }

    public void AskRename(SnapshotItemViewModel item)
    {
        if (!item.CanTag) return;
        Prompt(
            "Name this capture",
            "The name replaces the time in the list; the time still shows underneath. "
            + "Leave it empty to go back to the time.",
            item.Name ?? "",
            "Save",
            name => Rename(item, name));
    }

    void Rename(SnapshotItemViewModel item, string name)
    {
        name = name.Trim();
        CaptureMetaService.SetName(item.Path, name);
        Log(name.Length == 0
            ? $"Cleared the name on the {item.Taken:HH:mm:ss} capture."
            : $"Named the {item.Taken:HH:mm:ss} capture \"{name}\".");
        RefreshSnapshots();
    }

    void ClearLog()
    {
        LogEntries.Clear();
        ClearLogCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Nothing to clear once every remaining row is a favourite.</summary>
    bool HasClearableHistory => Snapshots.Any(s => !s.IsFavorite);

    void AskClearHistory()
    {
        var going = Snapshots.Count(s => !s.IsFavorite);
        var kept = Snapshots.Count - going;
        var pinned = Snapshots.Any(s => s.IsLastSession && !s.IsFavorite);
        Confirm(
            "Clear capture history?",
            $"Deletes {going} {(going == 1 ? "capture" : "captures")} from disk"
            + (pinned ? ", including the pinned pre-shutdown session" : "")
            + (kept > 0 ? $", and keeps your {kept} favourite{(kept == 1 ? "" : "s")}" : "")
            + ". This can't be undone.",
            "Clear history",
            ClearHistory);
    }

    /// <summary>
    /// Deletes exactly the files behind the rows the sidebar is showing, so
    /// "clear" can't quietly take something the user was never told about —
    /// minus the favourites, which were kept on purpose.
    /// </summary>
    void ClearHistory()
    {
        int cleared = 0, failed = 0;
        foreach (var s in Snapshots.Where(s => !s.IsFavorite).ToList())
        {
            try { File.Delete(s.Path); CaptureMetaService.Forget(s.Path); cleared++; }
            catch { failed++; }
        }
        Log(failed == 0
            ? $"Cleared capture history — {cleared} captures deleted."
            : $"Cleared {cleared} captures; {failed} could not be deleted.");
        _loadedPath = null;
        RefreshSnapshots();
    }

    // ---- Confirmation prompt -------------------------------------------
    // One prompt reused by whatever needs it: the caller supplies the words
    // and the action, and nothing destructive runs until Accept fires. It has a
    // second mode that asks for a line of text — same overlay, because a
    // MessageBox or a second dialog would arrive as a light-mode system window
    // on top of a dark, custom-chromed one.

    Action? _confirmAction;
    Action<string>? _promptAction;

    bool _showConfirm;
    public bool ShowConfirm
    {
        get => _showConfirm;
        private set { _showConfirm = value; OnPropertyChanged(); }
    }

    string _confirmTitle = "";
    public string ConfirmTitle
    {
        get => _confirmTitle;
        private set { _confirmTitle = value; OnPropertyChanged(); }
    }

    string _confirmMessage = "";
    public string ConfirmMessage
    {
        get => _confirmMessage;
        private set { _confirmMessage = value; OnPropertyChanged(); }
    }

    string _confirmAcceptLabel = "";
    public string ConfirmAcceptLabel
    {
        get => _confirmAcceptLabel;
        private set { _confirmAcceptLabel = value; OnPropertyChanged(); }
    }

    /// <summary>True while the prompt is asking for text rather than a yes/no.</summary>
    bool _confirmIsInput;
    public bool ConfirmIsInput
    {
        get => _confirmIsInput;
        private set { _confirmIsInput = value; OnPropertyChanged(); }
    }

    string _confirmInput = "";
    public string ConfirmInput
    {
        get => _confirmInput;
        set { _confirmInput = value; OnPropertyChanged(); }
    }

    void Confirm(string title, string message, string acceptLabel, Action onAccept)
    {
        _promptAction = null;
        _confirmAction = onAccept;
        Open(title, message, acceptLabel, input: false);
    }

    void Prompt(string title, string message, string initialValue, string acceptLabel,
        Action<string> onAccept)
    {
        _confirmAction = null;
        _promptAction = onAccept;
        ConfirmInput = initialValue;
        Open(title, message, acceptLabel, input: true);
    }

    void Open(string title, string message, string acceptLabel, bool input)
    {
        ConfirmTitle = title;
        ConfirmMessage = message;
        ConfirmAcceptLabel = acceptLabel;
        ConfirmIsInput = input;
        ShowConfirm = true;
    }

    void AcceptConfirm()
    {
        var action = _confirmAction;
        var prompt = _promptAction;
        var value = ConfirmInput;
        CloseConfirm();
        action?.Invoke();
        prompt?.Invoke(value);
    }

    void CloseConfirm()
    {
        _confirmAction = null;
        _promptAction = null;
        ShowConfirm = false;
    }

    void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Paths.DataDir}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    /// <summary>Appends a timestamped line to the activity log (any thread).</summary>
    public void Log(string message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) { dispatcher.Invoke(() => Log(message)); return; }
        LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogEntries.Count > 400) LogEntries.RemoveAt(0);
        ClearLogCommand.RaiseCanExecuteChanged();
    }
}
