using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Restage.Models;
using Restage.Native;
using Restage.Services;

namespace Restage.ViewModels;

/// <summary>
/// Root view model: the workspace gallery and its details pane, the capture,
/// restore, desktop-switch and confirmation overlays, settings, and the log.
/// </summary>
public class MainViewModel : ViewModelBase
{
    readonly AppSettings _settings;
    readonly IAppHost _host;
    readonly HotkeyService _hotkeys;
    readonly string _currentProfileId;
    CancellationTokenSource? _restoreCts;
    Task? _restoreTask;
    WorkspaceCaptureService.Result? _pendingCapture;
    WorkspaceCardViewModel? _pendingUpdateTarget;
    TaskCompletionSource<bool>? _desktopChoice;

    public MainViewModel(AppSettings settings, IAppHost host, HotkeyService hotkeys)
    {
        _settings = settings;
        _host = host;
        _hotkeys = hotkeys;
        _hotkeys.Pressed += OnHotkey;
        AppHotkey = new HotkeyRowViewModel(HotkeyActions.OpenApp, TextFor(HotkeyActions.OpenApp));
        AppHotkey.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HotkeyRowViewModel.Text)) OnPropertyChanged(nameof(AppHotkeyLabel));
        };
        _currentProfileId = DisplayProfiles.Current().Id;
        if (!Enum.IsDefined(_settings.DesktopSwitch)) _settings.DesktopSwitch = DesktopSwitchMode.Ask;
        ThemeService.Applied += () =>
        {
            OnPropertyChanged(nameof(IsDarkTheme));
            OnPropertyChanged(nameof(ThemeTooltip));
            OnPropertyChanged(nameof(FollowSystemTheme));
        };

        NewWorkspaceCommand = new RelayCommand(async _ => await BeginCaptureAsync(null), _ => !IsBusy);
        SelectCommand = new RelayCommand(p => Select(p as WorkspaceCardViewModel));
        RestoreCommand = new RelayCommand(async p => await RestoreAsync(Card(p)), _ => !IsBusy);
        UpdateCommand = new RelayCommand(async p => await BeginCaptureAsync(Card(p)), _ => !IsBusy);
        DeleteCommand = new RelayCommand(p => AskDelete(Card(p)));
        OpenFolderCommand = new RelayCommand(p => OpenFolder(WorkspaceStore.DirectoryFor(Card(p).Id)));
        NextPolicyCommand = new RelayCommand(p => (p as WorkspaceItemViewModel)?.NextPolicy());

        CaptureSaveCommand = new RelayCommand(_ => SaveCapture(), _ => CanSaveCapture);
        CaptureCancelCommand = new RelayCommand(_ => CancelCapture());
        CancelRestoreCommand = new RelayCommand(_ => _restoreCts?.Cancel(), _ => IsRestoring);
        CloseRestoreCommand = new RelayCommand(_ => ShowRestorePanel = false, _ => !IsRestoring);
        OpenTaskViewCommand = new RelayCommand(_ => OpenTaskView());
        RestoreHereCommand = new RelayCommand(_ => _desktopChoice?.TrySetResult(false));
        CancelDesktopCommand = new RelayCommand(_ => { _desktopChoice?.TrySetResult(false); _restoreCts?.Cancel(); });

        OpenDataFolderCommand = new RelayCommand(_ => OpenFolder(Paths.DataDir));
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        ClearLogCommand = new RelayCommand(_ => ClearLog(), _ => LogEntries.Count > 0);
        ConfirmAcceptCommand = new RelayCommand(_ => AcceptConfirm());
        ConfirmCancelCommand = new RelayCommand(_ => CloseConfirm());
        ClearHotkeyCommand = new RelayCommand(p => { if (p is HotkeyRowViewModel row) RecordHotkey(row, null); });
        EditCommand = new RelayCommand(p => OpenEdit(Card(p)));
        EditAppHotkeyCommand = new RelayCommand(_ => OpenAppHotkeyEdit());
        EditSaveCommand = new RelayCommand(_ => SaveEdit(), _ => EditingApp || !string.IsNullOrWhiteSpace(EditName));
        EditCancelCommand = new RelayCommand(_ => CloseEdit());

        RefreshWorkspaces();
        Log(_settings.ShowTrayIcon
            ? "Ready. Closing the window keeps Restage in the system tray."
            : "Ready. Closing the window keeps Restage running; start it again to open this window.");

        // "Opened 5 minutes ago" has to age; once a minute is plenty.
        _clock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _clock.Tick += (_, _) => { foreach (var card in Workspaces) card.RefreshTimes(); };
        _clock.Start();

        // Workspaces saved or deleted on disk by the CLI, or by the API's
        // callers, show up without a restart. Half a second of quiet first:
        // a save is several files, and our own saves come through here too.
        _storeChanged.Tick += (_, _) =>
        {
            _storeChanged.Stop();
            if (IsBusy) { _storeChanged.Start(); return; }
            RefreshWorkspaces();
        };
        _watcher = WatchStore();
    }

    readonly System.Windows.Threading.DispatcherTimer _clock;
    readonly System.Windows.Threading.DispatcherTimer _storeChanged = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly FileSystemWatcher? _watcher;

    FileSystemWatcher? WatchStore()
    {
        try
        {
            Directory.CreateDirectory(WorkspaceStore.Root);
            var watcher = new FileSystemWatcher(WorkspaceStore.Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            void Bump() => Application.Current?.Dispatcher.BeginInvoke(() => { _storeChanged.Stop(); _storeChanged.Start(); });
            watcher.Changed += (_, _) => Bump();
            watcher.Created += (_, _) => Bump();
            watcher.Deleted += (_, _) => Bump();
            watcher.Renamed += (_, _) => Bump();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch { return null; }
    }

    static WorkspaceCardViewModel Card(object? p) => (WorkspaceCardViewModel)p!;

    // ---- Gallery -----------------------------------------------------------

    public ObservableCollection<WorkspaceCardViewModel> Workspaces { get; } = new();

    public bool HasWorkspaces => Workspaces.Count > 0;
    public string WorkspaceCountText => Workspaces.Count switch
    {
        0 => "No workspaces yet",
        1 => "1 workspace",
        var n => $"{n} workspaces",
    };

    WorkspaceCardViewModel? _selected;
    public WorkspaceCardViewModel? Selected
    {
        get => _selected;
        private set
        {
            if (_selected == value) return;
            if (_selected != null) _selected.IsSelected = false;
            _selected = value;
            if (_selected != null) _selected.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected != null;

    public RelayCommand NewWorkspaceCommand { get; }
    public RelayCommand SelectCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand UpdateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand NextPolicyCommand { get; }

    void Select(WorkspaceCardViewModel? card) => Selected = card;

    /// <summary>Rebuilds the gallery from disk, keeping the selection when possible.</summary>
    public void RefreshWorkspaces()
    {
        var selectedId = Selected?.Id;
        Workspaces.Clear();
        foreach (var ws in WorkspaceStore.List())
            Workspaces.Add(new WorkspaceCardViewModel(ws, _currentProfileId, SaveWorkspace)
            {
                Hotkey = new HotkeyRowViewModel(HotkeyActions.Workspace(ws.Id), HotkeyTextFor(ws.Id)),
            });
        Selected = Workspaces.FirstOrDefault(w => w.Id == selectedId);
        ApplyHotkeys(); // also marks the new cards' hotkeys Windows refuses
        OnPropertyChanged(nameof(HasWorkspaces));
        OnPropertyChanged(nameof(WorkspaceCountText));
    }

    /// <summary>Persists item-level edits (enabled flags, policies) made in the details pane.</summary>
    void SaveWorkspace(WorkspaceCardViewModel card)
    {
        try { WorkspaceStore.Save(card.Model); }
        catch (Exception ex) { Log($"Could not save \"{card.Name}\": {ex.Message}"); }
    }

    // ---- Busy state --------------------------------------------------------

    bool _isBusy;
    /// <summary>True while capturing or restoring; the gallery's actions wait.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            NewWorkspaceCommand.RaiseCanExecuteChanged();
            RestoreCommand.RaiseCanExecuteChanged();
            UpdateCommand.RaiseCanExecuteChanged();
        }
    }

    // ---- Capture (new workspace / update) -----------------------------------

    public ObservableCollection<CaptureItemViewModel> CaptureItems { get; } = new();

    bool _showCapture;
    public bool ShowCapture { get => _showCapture; private set => SetProperty(ref _showCapture, value); }

    bool _isCapturing;
    public bool IsCapturing { get => _isCapturing; private set => SetProperty(ref _isCapturing, value); }

    string _captureName = "";
    public string CaptureName
    {
        get => _captureName;
        set { if (SetProperty(ref _captureName, value)) CaptureSaveCommand.RaiseCanExecuteChanged(); }
    }

    string _captureTitle = "";
    public string CaptureTitle { get => _captureTitle; private set => SetProperty(ref _captureTitle, value); }

    string _captureSummary = "";
    public string CaptureSummary { get => _captureSummary; private set => SetProperty(ref _captureSummary, value); }

    public string CaptureCount => $"{CaptureItems.Count(i => i.IsSelected)} of {CaptureItems.Count} selected";

    public bool? CaptureAllSelected
    {
        get
        {
            if (CaptureItems.Count == 0) return false;
            var n = CaptureItems.Count(i => i.IsSelected);
            return n == 0 ? false : n == CaptureItems.Count ? true : null;
        }
        set
        {
            var on = value == true;
            foreach (var i in CaptureItems) i.IsSelected = on;
            OnPropertyChanged();
        }
    }

    bool CanSaveCapture => _pendingCapture != null && !string.IsNullOrWhiteSpace(CaptureName) && CaptureItems.Any(i => i.IsSelected);

    public RelayCommand CaptureSaveCommand { get; }
    public RelayCommand CaptureCancelCommand { get; }

    /// <summary>Photographs the desktop (window hidden), then shows the found windows for review.</summary>
    async Task BeginCaptureAsync(WorkspaceCardViewModel? update)
    {
        if (IsBusy) return;
        IsBusy = true;
        IsCapturing = true;
        _pendingUpdateTarget = update;
        Log(update == null ? "Capturing the desktop…" : $"Re-capturing \"{update.Name}\"…");
        try
        {
            _host.HideMain();
            await Task.Delay(450); // let the compositor drop our window before the screenshot
            var existing = update?.Model;
            var name = update?.Name ?? "";
            _pendingCapture = await Task.Run(() => WorkspaceCaptureService.Capture(name, existing));
        }
        catch (Exception ex)
        {
            Log($"Capture failed: {ex.Message}");
            _pendingCapture = null;
        }
        finally
        {
            _host.ShowMain();
            IsCapturing = false;
        }
        if (_pendingCapture == null) { IsBusy = false; return; }

        var ws = _pendingCapture.Workspace;
        CaptureItems.Clear();
        foreach (var item in ws.Items)
        {
            var vm = new CaptureItemViewModel(item);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(CaptureItemViewModel.IsSelected)) return;
                OnPropertyChanged(nameof(CaptureAllSelected));
                OnPropertyChanged(nameof(CaptureCount));
                CaptureSaveCommand.RaiseCanExecuteChanged();
            };
            CaptureItems.Add(vm);
        }
        CaptureTitle = update == null ? "New workspace" : $"Update \"{update.Name}\"";
        CaptureName = update?.Name ?? SuggestName(ws);
        CaptureSummary = $"Desktop: {ws.Desktop?.DisplayName ?? "unknown"} · Displays: {_pendingCapture.Profile.Name}"
            + (string.IsNullOrEmpty(ws.Wallpaper) ? "" : $" · Wallpaper: {Path.GetFileName(ws.Wallpaper)}");
        OnPropertyChanged(nameof(CaptureAllSelected));
        OnPropertyChanged(nameof(CaptureCount));
        CaptureSaveCommand.RaiseCanExecuteChanged();
        ShowCapture = true;
    }

    static string SuggestName(Workspace ws) =>
        ws.Desktop?.Name is { Length: > 0 } d ? d : $"Workspace {DateTime.Now:ddd HH:mm}";

    void SaveCapture()
    {
        if (_pendingCapture == null) return;
        var result = _pendingCapture;
        var ws = result.Workspace;
        ws.Name = CaptureName.Trim();
        var keep = CaptureItems.Where(i => i.IsSelected).Select(i => i.Item).ToHashSet();
        ws.Items = ws.Items.Where(keep.Contains).ToList();
        try
        {
            WorkspaceStore.Save(ws, result.ThumbnailPng);
            Log(_pendingUpdateTarget == null
                ? $"Saved \"{ws.Name}\" with {ws.Items.Count} items."
                : $"Updated \"{ws.Name}\": {ws.Items.Count} items, layout for {result.Profile.Name}.");
        }
        catch (Exception ex) { Log($"Save failed: {ex.Message}"); }
        CloseCapture();
        RefreshWorkspaces();
        Selected = Workspaces.FirstOrDefault(w => w.Id == ws.Id);
    }

    void CancelCapture()
    {
        Log("Capture discarded.");
        CloseCapture();
    }

    void CloseCapture()
    {
        ShowCapture = false;
        _pendingCapture = null;
        _pendingUpdateTarget = null;
        CaptureItems.Clear();
        IsBusy = false;
    }

    // ---- Restore ------------------------------------------------------------

    public ObservableCollection<RestoreRowViewModel> RestoreProgress { get; } = new();

    bool _showRestorePanel;
    public bool ShowRestorePanel { get => _showRestorePanel; private set => SetProperty(ref _showRestorePanel, value); }

    bool _isRestoring;
    public bool IsRestoring
    {
        get => _isRestoring;
        private set
        {
            if (!SetProperty(ref _isRestoring, value)) return;
            CancelRestoreCommand.RaiseCanExecuteChanged();
            CloseRestoreCommand.RaiseCanExecuteChanged();
        }
    }

    string _restoreTitle = "";
    public string RestoreTitle { get => _restoreTitle; private set => SetProperty(ref _restoreTitle, value); }

    string _restoreNote = "";
    /// <summary>Run-level messages (desktop, wallpaper, finish) under the title.</summary>
    public string RestoreNote { get => _restoreNote; private set => SetProperty(ref _restoreNote, value); }

    int _restoreDone, _restoreTotal;
    public string RestoreCounter => $"{_restoreDone} of {_restoreTotal}";
    public double RestoreFraction => _restoreTotal == 0 ? 0 : (double)_restoreDone / _restoreTotal;

    public RelayCommand CancelRestoreCommand { get; }
    public RelayCommand CloseRestoreCommand { get; }

    /// <summary>Entry point for the command line and the restage: URI; matches by name or id.</summary>
    public async Task RestoreByNameAsync(string nameOrId)
    {
        var card = Workspaces.FirstOrDefault(w => string.Equals(w.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
                   ?? Workspaces.FirstOrDefault(w => w.Id == nameOrId);
        if (card == null) { Log($"No workspace named \"{nameOrId}\"."); _host.ShowMain(); return; }
        await RestoreAsync(card);
    }

    Task RestoreAsync(WorkspaceCardViewModel card, bool bringToFront = false, bool? force = null, bool here = false)
    {
        if (IsBusy) return Task.CompletedTask;
        return _restoreTask = RunRestoreAsync(card, bringToFront, force, here);
    }

    /// <summary>
    /// A start by hotkey or command also raises the workspace's windows: that
    /// is what switching to it means. Force and here override the settings
    /// for this run (the API and the CLI ask for them per call).
    /// </summary>
    async Task RunRestoreAsync(WorkspaceCardViewModel card, bool bringToFront, bool? force, bool here)
    {
        Selected = card;
        IsBusy = true;
        IsRestoring = true;
        RestoreTitle = card.Name;
        RestoreNote = card.LacksCurrentProfile
            ? "No layout saved for the current monitors; using the closest one."
            : "Apps open one at a time so your PC stays responsive.";
        RestoreProgress.Clear();
        foreach (var item in card.Model.Items.Where(i => i.Enabled))
            RestoreProgress.Add(new RestoreRowViewModel(item));
        _restoreDone = 0;
        _restoreTotal = RestoreProgress.Count;
        OnPropertyChanged(nameof(RestoreCounter));
        OnPropertyChanged(nameof(RestoreFraction));
        ShowRestorePanel = true;
        Log($"Starting \"{card.Name}\" — {_restoreTotal} items…");
        try { WorkspaceStore.MarkOpened(card.Model); } catch (Exception ex) { Log($"Could not note the start: {ex.Message}"); }
        card.RefreshTimes();

        _restoreCts = new CancellationTokenSource();
        var options = new RestoreOptions
        {
            Force = force ?? _settings.ForceLaunch,
            SwitchDesktop = here || _settings.DesktopSwitch == DesktopSwitchMode.Never ? null : SwitchDesktopAsync,
            BringToFront = bringToFront,
        };
        var progress = new Progress<RestoreEvent>(OnRestoreEvent);
        var ws = card.Model;
        try
        {
            await Task.Run(() => WorkspaceRestoreService.RestoreAsync(ws, options, progress, _restoreCts.Token));
        }
        catch (OperationCanceledException) { Log("Start cancelled."); RestoreNote = "Cancelled."; }
        catch (Exception ex) { Log($"Start failed: {ex.Message}"); RestoreNote = $"Failed: {ex.Message}"; }
        finally
        {
            IsRestoring = false;
            IsBusy = false;
            _restoreCts.Dispose();
            _restoreCts = null;
        }
    }

    void OnRestoreEvent(RestoreEvent e)
    {
        if (e.ItemId == null)
        {
            RestoreNote = e.Message;
            Log(e.Message);
            return;
        }
        var row = RestoreProgress.FirstOrDefault(r => r.ItemId == e.ItemId);
        if (row == null) return;
        var wasTerminal = row.IsTerminal;
        row.Message = e.Message;
        row.Status = e.Status;
        if (e.Status is RestoreStatus.Failed or RestoreStatus.Skipped) Log($"{row.Name}: {e.Message}");
        if (!wasTerminal && row.IsTerminal)
        {
            _restoreDone++;
            OnPropertyChanged(nameof(RestoreCounter));
            OnPropertyChanged(nameof(RestoreFraction));
        }
    }

    // ---- Desktop switch prompt ----------------------------------------------

    bool _showDesktopPrompt;
    public bool ShowDesktopPrompt { get => _showDesktopPrompt; private set => SetProperty(ref _showDesktopPrompt, value); }

    string _desktopPromptName = "";
    public string DesktopPromptName { get => _desktopPromptName; private set => SetProperty(ref _desktopPromptName, value); }

    public RelayCommand OpenTaskViewCommand { get; }
    public RelayCommand RestoreHereCommand { get; }
    public RelayCommand CancelDesktopCommand { get; }

    /// <summary>
    /// Called by the restore service (on a worker thread) when the workspace
    /// lives on another desktop. Windows has no public API to switch, so the
    /// user does it, guided by a prompt. Returns true once that desktop is
    /// current; our own window follows it.
    /// </summary>
    async Task<bool> SwitchDesktopAsync(DesktopRef desktop, CancellationToken ct)
    {
        if (desktop.Id is not { } id) return false;
        var dispatcher = Application.Current.Dispatcher;
        var wait = VirtualDesktops.WaitUntilCurrentAsync(id, TimeSpan.FromMinutes(5), ct);

        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await dispatcher.InvokeAsync(() =>
        {
            _desktopChoice = choice;
            DesktopPromptName = desktop.DisplayName;
            ShowDesktopPrompt = true;
            _host.ShowMain();
        });
        var first = await Task.WhenAny(choice.Task, wait);
        var switched = first == wait ? await wait : await choice.Task && VirtualDesktops.IsCurrent(id);
        await dispatcher.InvokeAsync(() => { ShowDesktopPrompt = false; _desktopChoice = null; });

        if (switched && VirtualDesktops.IsCurrent(id))
        {
            // Bring our window along so the progress stays in view.
            await dispatcher.InvokeAsync(() =>
            {
                var hwnd = _host.MainHandle;
                if (hwnd != IntPtr.Zero) VirtualDesktops.MoveWindowToDesktop(hwnd, id);
                _host.ShowMain();
            });
            return true;
        }
        return false;
    }

    static void OpenTaskView()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // ---- Rename / delete (rename: the edit panel, or a click on the details pane's title) ----

    /// <summary>Renames a workspace (edit panel or title); an empty or unchanged name is ignored.</summary>
    public void Rename(WorkspaceCardViewModel card, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == card.Name) return;
        card.Model.Name = name;
        SaveWorkspace(card);
        card.Reload();
        Log($"Renamed to \"{name}\".");
    }

    void AskDelete(WorkspaceCardViewModel card)
    {
        Confirm("Delete workspace?", $"\"{card.Name}\" and its thumbnail are removed from disk. Open windows are not affected.",
            "Delete", () =>
            {
                try
                {
                    WorkspaceStore.Delete(card.Id);
                    Log($"Deleted \"{card.Name}\".");
                    if (_settings.Hotkeys.Remove(HotkeyActions.Workspace(card.Id)))
                        SettingsService.Save(_settings);
                }
                catch (Exception ex) { Log($"Delete failed: {ex.Message}"); }
                if (Selected == card) Selected = null;
                RefreshWorkspaces();
            });
    }

    // ---- Settings ----------------------------------------------------------

    public bool StartWithWindows
    {
        get => SettingsService.GetStartWithWindows();
        set
        {
            try
            {
                SettingsService.SetStartWithWindows(value);
                Log(value ? "Restage will run at startup (in the tray)." : "Removed from startup.");
            }
            catch (Exception ex) { Log($"Startup setting failed: {ex.Message}"); }
            OnPropertyChanged();
        }
    }

    public bool ShowTrayIcon
    {
        get => _settings.ShowTrayIcon;
        set
        {
            if (_settings.ShowTrayIcon == value) return;
            _settings.ShowTrayIcon = value;
            SettingsService.Save(_settings);
            _host.RefreshTray();
            if (!value) Log("Tray icon hidden. Start Restage again (Start menu) to open this window.");
            OnPropertyChanged();
        }
    }

    public bool MonochromeTrayIcon
    {
        get => _settings.MonochromeTrayIcon;
        set
        {
            if (_settings.MonochromeTrayIcon == value) return;
            _settings.MonochromeTrayIcon = value;
            SettingsService.Save(_settings);
            _host.RefreshTray();
            OnPropertyChanged();
        }
    }

    public bool ForceLaunch
    {
        get => _settings.ForceLaunch;
        set { if (_settings.ForceLaunch == value) return; _settings.ForceLaunch = value; SettingsService.Save(_settings); OnPropertyChanged(); }
    }

    /// <summary>"Ask", "Wait" or "Never"; a string so the radio buttons can bind through EqualsConverter.</summary>
    public string DesktopSwitchText
    {
        get => _settings.DesktopSwitch.ToString();
        set
        {
            if (!Enum.TryParse<DesktopSwitchMode>(value, out var mode) || mode == _settings.DesktopSwitch) return;
            _settings.DesktopSwitch = mode;
            SettingsService.Save(_settings);
            OnPropertyChanged();
        }
    }

    public bool IsDarkTheme => ThemeService.IsDark;
    public string ThemeTooltip => IsDarkTheme ? "Switch to light mode" : "Switch to dark mode";

    /// <summary>Follow Windows' light/dark setting; off keeps whichever mode is showing now, as a fixed choice.</summary>
    public bool FollowSystemTheme
    {
        get => _settings.Theme is not (ThemeService.Light or ThemeService.Dark);
        set
        {
            var wanted = value ? ThemeService.System : IsDarkTheme ? ThemeService.Dark : ThemeService.Light;
            if (_settings.Theme == wanted) return;
            _settings.Theme = wanted;
            SettingsService.Save(_settings);
            ThemeService.Apply(wanted);
            OnPropertyChanged();
        }
    }

    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }

    void ToggleTheme()
    {
        _settings.Theme = IsDarkTheme ? ThemeService.Light : ThemeService.Dark;
        SettingsService.Save(_settings);
        ThemeService.Apply(_settings.Theme);
    }

    static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{path}\"", UseShellExecute = true });
        }
        catch { }
    }

    // ---- Global hotkeys --------------------------------------------------------
    // One per workspace, set in its edit panel, and one that opens Restage, set
    // from the settings panel in the same edit panel. Kept in AppSettings.Hotkeys
    // and registered with Windows at startup and after every change. A workspace's
    // starts it, or switches to it: open windows are placed and raised.

    IEnumerable<HotkeyRowViewModel> HotkeyRows => Workspaces.Select(w => w.Hotkey).Append(AppHotkey);

    public RelayCommand ClearHotkeyCommand { get; }

    /// <summary>The shortcut that opens Restage's window.</summary>
    public HotkeyRowViewModel AppHotkey { get; }

    /// <summary>The settings panel's row: the shortcut, or an invitation to set one.</summary>
    public string AppHotkeyLabel => AppHotkey.HasHotkey ? $"Open Restage with {AppHotkey.Text}" : "Shortcut to open Restage";

    /// <summary>The bound combination of a workspace as text, or "" (also for garbage in a hand-edited file).</summary>
    public string HotkeyTextFor(string workspaceId) => TextFor(HotkeyActions.Workspace(workspaceId));

    string TextFor(string action) =>
        _settings.Hotkeys.TryGetValue(action, out var text) && Hotkey.TryParse(text, out var hk) ? hk.ToString() : "";

    /// <summary>
    /// Registers every bound hotkey with Windows and marks the rows Windows
    /// refused. Hotkeys of workspaces that no longer exist are skipped.
    /// </summary>
    public void ApplyHotkeys()
    {
        _hotkeys.UnregisterAll();
        var ids = Workspaces.Select(w => w.Id).ToHashSet();
        var refused = new HashSet<string>();
        foreach (var (action, text) in _settings.Hotkeys)
        {
            if (action != HotkeyActions.OpenApp && (HotkeyActions.WorkspaceId(action) is not { } id || !ids.Contains(id))) continue;
            if (!Hotkey.TryParse(text, out var hk)) continue;
            if (!_hotkeys.Register(action, hk)) refused.Add(action);
        }
        foreach (var row in HotkeyRows)
            row.Problem = refused.Contains(row.Action) ? "Taken by another app or by Windows" : "";
    }

    // ---- Edit panel: name and keyboard shortcut ---------------------------------
    // Works on copies; Save writes both, Cancel drops both.

    // By id, not by card: the gallery can be rebuilt (a change on disk) while the panel is open.
    string? _editingId;

    bool _showEdit;
    public bool ShowEdit { get => _showEdit; private set => SetProperty(ref _showEdit, value); }

    string _editName = "";
    public string EditName
    {
        get => _editName;
        set { if (SetProperty(ref _editName, value)) EditSaveCommand.RaiseCanExecuteChanged(); }
    }

    HotkeyRowViewModel? _editHotkey;
    /// <summary>The shortcut being edited, a copy of the workspace's until Save.</summary>
    public HotkeyRowViewModel? EditHotkey { get => _editHotkey; private set => SetProperty(ref _editHotkey, value); }

    /// <summary>True while the panel edits the open-Restage shortcut rather than a workspace.</summary>
    public bool EditingApp { get => _editingApp; private set { if (SetProperty(ref _editingApp, value)) OnEditModeChanged(); } }
    bool _editingApp;

    public string EditTitle => EditingApp ? "Open Restage" : "Edit workspace";

    public string EditHint => EditingApp
        ? "Opens Restage's window from anywhere in Windows, also while the tray icon is hidden. Click the box, then press Ctrl, Alt or Win plus a key; Backspace clears it."
        : "Starts the workspace from anywhere in Windows, or brings it to the front when it is already open. Click the box, then press Ctrl, Alt or Win plus a key; Backspace clears it.";

    void OnEditModeChanged()
    {
        OnPropertyChanged(nameof(EditTitle));
        OnPropertyChanged(nameof(EditHint));
        EditSaveCommand.RaiseCanExecuteChanged();
    }

    public RelayCommand EditCommand { get; }
    public RelayCommand EditAppHotkeyCommand { get; }
    public RelayCommand EditSaveCommand { get; }
    public RelayCommand EditCancelCommand { get; }

    void OpenEdit(WorkspaceCardViewModel card)
    {
        EditingApp = false;
        _editingId = card.Id;
        EditName = card.Name;
        EditHotkey = new HotkeyRowViewModel(card.Hotkey.Action, card.Hotkey.Text);
        ShowEdit = true;
    }

    void OpenAppHotkeyEdit()
    {
        EditingApp = true;
        _editingId = null;
        EditName = "";
        EditHotkey = new HotkeyRowViewModel(AppHotkey.Action, AppHotkey.Text);
        ShowEdit = true;
    }

    void SaveEdit()
    {
        if (EditingApp && EditHotkey is { } appEdit)
        {
            if (appEdit.Text != AppHotkey.Text)
            {
                if (appEdit.Text.Length == 0) _settings.Hotkeys.Remove(AppHotkey.Action);
                else _settings.Hotkeys[AppHotkey.Action] = appEdit.Text;
                SettingsService.Save(_settings);
                AppHotkey.Text = appEdit.Text;
                ApplyHotkeys();
                Log(appEdit.Text.Length == 0
                    ? "Restage no longer has a shortcut to open it."
                    : $"{appEdit.Text} now opens Restage.");
            }
            CloseEdit();
            return;
        }
        if (EditHotkey is not { } edited || Workspaces.FirstOrDefault(w => w.Id == _editingId) is not { } card) { CloseEdit(); return; }
        Rename(card, EditName);
        if (edited.Text != card.Hotkey.Text)
        {
            if (edited.Text.Length == 0) _settings.Hotkeys.Remove(card.Hotkey.Action);
            else _settings.Hotkeys[card.Hotkey.Action] = edited.Text;
            SettingsService.Save(_settings);
            card.Hotkey.Text = edited.Text;
            ApplyHotkeys();
            Log(edited.Text.Length == 0
                ? $"\"{card.Name}\" no longer has a keyboard shortcut."
                : $"\"{card.Name}\" now starts with {edited.Text}.");
        }
        CloseEdit();
    }

    void CloseEdit()
    {
        ShowEdit = false;
        _editingId = null;
        EditingApp = false;
    }

    /// <summary>
    /// While the box records, Restage's own hotkeys are released, or pressing a
    /// combination that is already bound would trigger it instead of reaching the box.
    /// </summary>
    public void BeginRecording(HotkeyRowViewModel row)
    {
        row.IsRecording = true;
        _hotkeys.UnregisterAll();
    }

    public void EndRecording()
    {
        if (EditHotkey != null) EditHotkey.IsRecording = false;
        ApplyHotkeys();
    }

    /// <summary>
    /// Takes a recorded combination into the edit copy (null clears it). Refused
    /// when another workspace has it, or when Windows will not give it: while
    /// recording Restage holds no hotkeys, so a failed trial registration means
    /// another app or the shell has it.
    /// </summary>
    public void RecordHotkey(HotkeyRowViewModel row, Hotkey? hotkey)
    {
        row.Problem = "";
        if (hotkey is not { } hk) { row.Text = ""; return; }
        var text = hk.ToString();
        var owner = Workspaces.FirstOrDefault(w => (EditingApp || w.Id != _editingId) && w.Hotkey.Text == text);
        if (owner != null) { row.Problem = $"Already used for \"{owner.Name}\""; return; }
        if (!EditingApp && AppHotkey.Text == text) { row.Problem = "Already used to open Restage"; return; }
        if (!_hotkeys.IsAvailable(hk)) { row.Problem = "Taken by another app or by Windows"; return; }
        row.Text = text;
    }

    async void OnHotkey(string action)
    {
        // WM_HOTKEY gives Restage the right to take the foreground, so the window comes up on top.
        if (action == HotkeyActions.OpenApp) { _host.ShowMain(); return; }
        if (HotkeyActions.WorkspaceId(action) is { } id && Workspaces.FirstOrDefault(w => w.Id == id) is { } card)
            await StartByCommandAsync(card, bringToFront: true);
    }

    // ---- Commands from outside: hotkeys and the local API ----------------------

    /// <summary>
    /// A start by hotkey or API. A start still running is cancelled first, so
    /// one after another switches without waiting; a capture is not interrupted.
    /// </summary>
    public async Task StartByCommandAsync(WorkspaceCardViewModel card, bool bringToFront, bool force = false, bool here = false)
    {
        if (IsRestoring && _restoreTask != null)
        {
            _restoreCts?.Cancel();
            try { await _restoreTask; } catch { }
        }
        if (IsBusy) { Log($"Busy with a capture; \"{card.Name}\" not started."); return; }
        await RestoreAsync(card, bringToFront, force ? true : null, here);
    }

    /// <summary>
    /// A capture asked for over the API: every window, saved at once without
    /// the review step. Null when a capture or start is already running.
    /// </summary>
    public async Task<Workspace?> CaptureByCommandAsync(string name)
    {
        if (IsBusy) return null;
        IsBusy = true;
        IsCapturing = true;
        var wasVisible = _host.IsMainVisible;
        try
        {
            if (wasVisible) { _host.HideMain(); await Task.Delay(450); }
            var result = await Task.Run(() => WorkspaceCaptureService.Capture(name));
            WorkspaceStore.Save(result.Workspace, result.ThumbnailPng);
            Log($"Saved \"{result.Workspace.Name}\" with {result.Workspace.Items.Count} items.");
            RefreshWorkspaces();
            Selected = Workspaces.FirstOrDefault(w => w.Id == result.Workspace.Id);
            return result.Workspace;
        }
        catch (Exception ex)
        {
            Log($"Capture failed: {ex.Message}");
            throw;
        }
        finally
        {
            if (wasVisible) _host.ShowMain();
            IsCapturing = false;
            IsBusy = false;
        }
    }

    // ---- Confirmation / prompt overlay ---------------------------------------
    // One overlay for yes/no questions (delete) and single-line text input: a
    // MessageBox would arrive as a light-mode system window on top of a dark,
    // custom-chromed one.

    Action? _confirmAction;
    Action<string>? _promptAction;

    bool _showConfirm;
    public bool ShowConfirm { get => _showConfirm; private set => SetProperty(ref _showConfirm, value); }

    string _confirmTitle = "";
    public string ConfirmTitle { get => _confirmTitle; private set => SetProperty(ref _confirmTitle, value); }

    string _confirmMessage = "";
    public string ConfirmMessage { get => _confirmMessage; private set => SetProperty(ref _confirmMessage, value); }

    string _confirmAcceptLabel = "";
    public string ConfirmAcceptLabel { get => _confirmAcceptLabel; private set => SetProperty(ref _confirmAcceptLabel, value); }

    bool _confirmIsInput;
    public bool ConfirmIsInput { get => _confirmIsInput; private set => SetProperty(ref _confirmIsInput, value); }

    string _confirmInput = "";
    public string ConfirmInput { get => _confirmInput; set => SetProperty(ref _confirmInput, value); }

    public RelayCommand ConfirmAcceptCommand { get; }
    public RelayCommand ConfirmCancelCommand { get; }

    void Confirm(string title, string message, string acceptLabel, Action onAccept)
    {
        _promptAction = null;
        _confirmAction = onAccept;
        Open(title, message, acceptLabel, input: false);
    }

    void Prompt(string title, string message, string initialValue, string acceptLabel, Action<string> onAccept)
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

    // ---- Activity log ---------------------------------------------------------

    public ObservableCollection<string> LogEntries { get; } = new();
    public RelayCommand ClearLogCommand { get; }

    void ClearLog()
    {
        LogEntries.Clear();
        ClearLogCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Appends a timestamped line to the activity log (any thread).</summary>
    public void Log(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess()) { dispatcher.Invoke(() => Log(message)); return; }
        LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogEntries.Count > 400) LogEntries.RemoveAt(0);
        ClearLogCommand.RaiseCanExecuteChanged();
    }
}
