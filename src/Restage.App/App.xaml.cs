using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Restage.Models;
using Restage.Services;
using Restage.ViewModels;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Restage;

/// <summary>
/// Composition root: single-instance guard, tray icon, the main view model, and
/// the "--restore &lt;name&gt;" / restage://restore/&lt;name&gt; entry points. The main
/// window is created on demand and the app keeps running in the tray when it
/// closes.
/// </summary>
public partial class App : Application, IAppHost
{
    const string MutexName = @"Local\Restage.SingleInstance";
    const string SignalName = @"Local\Restage.Signal";
    static readonly string RequestFile = Path.Combine(Paths.DataDir, "restore-request.txt");

    static Mutex? _singleInstance;
    static EventWaitHandle? _signal;
    WinForms.NotifyIcon? _tray;
    Drawing.Icon? _trayIcon;
    (bool? Light, bool Busy, bool TaskbarLight)? _trayIconKey;
    Views.MainWindow? _mainWindow;
    HotkeyService? _hotkeys;
    LocalApiServer? _api;

    public AppSettings Settings { get; private set; } = new();
    public MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>Semantic version shown to users and stamped into crash reports.</summary>
    public static string AppVersion { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.1.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        HookCrashLogging();

        var request = UriScheme.RestoreRequest(e.Args);

        // Single instance — skipped under a debugger (so F5 from an IDE always
        // runs) and with --multi (dev escape hatch). A second launch hands its
        // request to the running instance and exits.
        var skipGuard = e.Args.Contains("--multi") || Debugger.IsAttached;
        if (!skipGuard)
        {
            _singleInstance = new Mutex(true, MutexName, out var isFirst);
            if (!isFirst)
            {
                if (request != null) WriteRequest(request);
                try { EventWaitHandle.OpenExisting(SignalName).Set(); } catch { }
                Shutdown();
                return;
            }
            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            var waiter = new Thread(() =>
            {
                while (_signal.WaitOne())
                    Dispatcher.Invoke(OnSignalled);
            }) { IsBackground = true };
            waiter.Start();
        }

        Settings = SettingsService.Load();
        ThemeService.Apply(Settings.Theme);
        UriScheme.Register();

        _hotkeys = new HotkeyService();
        ViewModel = new MainViewModel(Settings, this, _hotkeys);
        _api = new LocalApiServer(ViewModel);
        _api.Start();

        _tray = new WinForms.NotifyIcon { Text = $"Restage {AppVersion}" };
        _tray.ContextMenuStrip = BuildTrayMenu();
        _tray.DoubleClick += (_, _) => ShowMain();
        RefreshTray();
        // The badge shows while a workspace's apps are being launched.
        ViewModel.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName is nameof(MainViewModel.IsRestoring)) RefreshTray();
        };
        // The monochrome icon and the badge follow the taskbar's light/dark mode.
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, ev) =>
        {
            if (ev.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.VisualStyle)
                Dispatcher.BeginInvoke(RefreshTray);
        };

        if (request != null)
            _ = ViewModel.RestoreByNameAsync(request);
        else if (!e.Args.Contains("--minimized"))
            ShowMain();
    }

    /// <summary>A second instance signalled: run its restore request, or just come to the front.</summary>
    void OnSignalled()
    {
        var request = ReadRequest();
        if (request != null) _ = ViewModel.RestoreByNameAsync(request);
        else ShowMain();
    }

    static void WriteRequest(string name)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllText(RequestFile, name);
        }
        catch { }
    }

    static string? ReadRequest()
    {
        try
        {
            if (!File.Exists(RequestFile)) return null;
            var name = File.ReadAllText(RequestFile).Trim();
            File.Delete(RequestFile);
            return name.Length > 0 ? name : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Writes every unhandled exception to %LOCALAPPDATA%\Restage\crash.log.
    /// UI-thread faults are swallowed after logging to keep the tray app alive.
    /// </summary>
    void HookCrashLogging()
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash("UI thread", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            LogCrash("AppDomain", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogCrash("Background task", ex.Exception);
            ex.SetObserved();
        };
    }

    static void LogCrash(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.AppendAllText(Paths.CrashLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Restage {AppVersion} — {source}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* never let crash reporting crash */ }
    }

    /// <summary>
    /// Tray menu in the app's own chrome (see Views.TrayMenuRenderer): open,
    /// one row per workspace to restore it, exit. Rebuilt on every open so it
    /// reflects the current workspaces and theme.
    /// </summary>
    WinForms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new Views.TrayMenuRenderer(),
            ShowCheckMargin = false,
            ShowImageMargin = true,
            Font = new Drawing.Font("Segoe UI", 9.5f),
        };
        var px = Math.Max(16, 16 * menu.DeviceDpi / 96);
        menu.ImageScalingSize = new Drawing.Size(px, px);
        menu.Padding = new WinForms.Padding(4, 6, 4, 6);

        void Rebuild()
        {
            foreach (WinForms.ToolStripItem old in menu.Items.Cast<WinForms.ToolStripItem>().ToList())
            {
                if (old is WinForms.ToolStripMenuItem mi) mi.Image?.Dispose();
                old.Dispose();
            }
            menu.Items.Clear();

            var open = new WinForms.ToolStripMenuItem("Open Restage") { Image = Views.TrayIcons.Render("IconAppWindow", "MutedBrush", px) };
            open.Click += (_, _) => ShowMain();
            menu.Items.Add(open);
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var workspaces = ViewModel.Workspaces.ToList();
            if (workspaces.Count == 0)
                menu.Items.Add(new WinForms.ToolStripMenuItem("No workspaces yet") { Enabled = false });
            foreach (var ws in workspaces)
            {
                var row = new WinForms.ToolStripMenuItem(ws.Name)
                {
                    Image = Views.TrayIcons.Render("IconLayoutGrid", "MutedBrush", px),
                    ShortcutKeyDisplayString = ViewModel.HotkeyTextFor(ws.Id),
                };
                var id = ws.Id;
                row.Click += (_, _) => _ = ViewModel.RestoreByNameAsync(id);
                menu.Items.Add(row);
            }
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var exit = new WinForms.ToolStripMenuItem("Exit") { Image = Views.TrayIcons.Render("IconPower", "MutedBrush", px) };
            exit.Click += (_, _) => ExitApp();
            menu.Items.Add(exit);

            foreach (WinForms.ToolStripItem item in menu.Items)
                if (item is WinForms.ToolStripMenuItem) item.Padding = new WinForms.Padding(2, 5, 2, 5);
        }

        Rebuild();
        menu.Opening += (_, _) => Rebuild();
        return menu;
    }

    /// <summary>
    /// Applies the tray settings: shown or hidden, colour or monochrome, and the
    /// busy badge while a workspace starts. The icon is redrawn only when one of
    /// those, or the taskbar's mode, changed; the old icon is disposed after the
    /// new one is in place.
    /// </summary>
    public void RefreshTray()
    {
        if (_tray == null) return;
        var taskbarLight = ThemeService.TaskbarUsesLightTheme();
        bool? light = Settings.MonochromeTrayIcon ? taskbarLight : null;
        var busy = ViewModel.IsRestoring;
        var key = (light, busy, taskbarLight);
        if (_trayIcon == null || key != _trayIconKey)
        {
            var old = _trayIcon;
            var plain = light is { } l ? Views.TrayGlyph.Render(l) : LoadTrayIcon();
            if (busy)
            {
                _trayIcon = Views.TrayGlyph.WithBusyBadge(plain, taskbarLight);
                plain.Dispose();
            }
            else _trayIcon = plain;
            _trayIconKey = key;
            _tray.Icon = _trayIcon;
            old?.Dispose();
        }
        var text = busy ? $"Restage: starting {ViewModel.RestoreTitle}…" : $"Restage {AppVersion}";
        _tray.Text = text.Length > 127 ? text[..126] + "…" : text;
        _tray.Visible = Settings.ShowTrayIcon;
    }

    /// <summary>The embedded app icon, falling back to the generic system icon.</summary>
    static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var sri = GetResourceStream(new Uri("pack://application:,,,/Assets/Restage.ico"));
            if (sri != null) return new Drawing.Icon(sri.Stream);
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }

    // ---- IAppHost ---------------------------------------------------------

    public void ShowMain()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new Views.MainWindow { DataContext = ViewModel };
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void HideMain() => _mainWindow?.Hide();

    public IntPtr MainHandle => _mainWindow == null ? IntPtr.Zero : new WindowInteropHelper(_mainWindow).Handle;

    public bool IsMainVisible => _mainWindow?.IsVisible == true;

    public bool IsMainActive =>
        _mainWindow is { IsVisible: true, IsActive: true } w && w.WindowState != WindowState.Minimized;

    /// <summary>Removes the tray icon and shuts the application down for real.</summary>
    public void ExitApp()
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _trayIcon?.Dispose();
        _trayIcon = null;
        _api?.Dispose();
        _api = null;
        _hotkeys?.Dispose();
        _hotkeys = null;
        Shutdown();
    }
}
