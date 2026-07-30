using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Rewake.Models;
using Rewake.Services;
using Rewake.ViewModels;
using Rewake.Views;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Rewake;

/// <summary>
/// Composition root: owns the single-instance guard, tray icon, auto-capture
/// timer, and the main view model. The main window is created on demand and the
/// app keeps running in the tray when it closes.
/// </summary>
public partial class App : Application
{
    static Mutex? _singleInstance;
    static EventWaitHandle? _showSignal;
    WinForms.NotifyIcon? _tray;
    DispatcherTimer? _timer;
    Views.MainWindow? _mainWindow;

    public AppSettings Settings { get; private set; } = new();
    public MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>Semantic version shown to users and stamped into crash reports.</summary>
    public static string AppVersion { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "1.0.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        HookCrashLogging();

        // Single instance — skipped under a debugger (so F5 from an IDE always
        // runs) and with --multi (dev escape hatch). A second launch signals the
        // running instance to show its window instead of dying silently.
        var skipGuard = e.Args.Contains("--multi") || System.Diagnostics.Debugger.IsAttached;
        if (!skipGuard)
        {
            _singleInstance = new Mutex(true, @"Local\Rewake.SingleInstance", out var isFirst);
            if (!isFirst)
            {
                try { EventWaitHandle.OpenExisting(@"Local\Rewake.ShowSignal").Set(); } catch { }
                Shutdown();
                return;
            }
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Rewake.ShowSignal");
            var waiter = new Thread(() =>
            {
                while (_showSignal.WaitOne())
                    Dispatcher.Invoke(ShowMain);
            })
            { IsBackground = true };
            waiter.Start();
        }

        Settings = SettingsService.Load();
        ThemeService.Apply(Settings.Theme);

        try { SnapshotService.PreserveLastSessionIfNewBoot(); } catch { }

        ViewModel = new MainViewModel(Settings);
        ViewModel.AutoCaptureChanged += SetupTimer;

        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = $"Rewake {AppVersion} — workspace snapshots"
        };
        _tray.ContextMenuStrip = BuildTrayMenu();
        _tray.DoubleClick += (_, _) => ShowMain();

        SetupTimer();

        if (!e.Args.Contains("--minimized")) ShowMain();
    }

    /// <summary>
    /// Writes every unhandled exception to %LOCALAPPDATA%\Rewake\crash.log so
    /// testers can attach it to a bug report. UI-thread faults are swallowed
    /// after logging to keep the tray app alive.
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
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Rewake {AppVersion} — {source}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* never let crash reporting crash */ }
    }

    /// <summary>
    /// Tray menu in the app's own chrome (see Views.TrayMenuRenderer), with the
    /// same Lucide icons as the window. Icons are re-rasterised on every open:
    /// that is when the current theme and Stay Awake state are known to be
    /// fresh, and four small bitmaps cost nothing.
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
        // Pre-scaled bitmaps, so WinForms doesn't resample them a second time.
        var px = Math.Max(16, 16 * menu.DeviceDpi / 96);
        menu.ImageScalingSize = new Drawing.Size(px, px);
        menu.Padding = new WinForms.Padding(4, 6, 4, 6);

        var open = new WinForms.ToolStripMenuItem("Open Rewake");
        open.Click += (_, _) => ShowMain();
        var capture = new WinForms.ToolStripMenuItem("Capture now");
        capture.Click += async (_, _) => await ViewModel.CaptureNowAsync();
        // Stay Awake outlives the window, so the tray is where a forgotten
        // keeper gets found and stopped without reopening anything.
        var stayAwake = new WinForms.ToolStripMenuItem("Stay awake");
        stayAwake.Click += (_, _) => ViewModel.ToggleStayAwake();
        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApp();

        foreach (var item in new[] { open, capture, stayAwake, exit })
            item.Padding = new WinForms.Padding(2, 5, 2, 5);

        menu.Items.Add(open);
        menu.Items.Add(capture);
        menu.Items.Add(stayAwake);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);

        void RefreshIcons()
        {
            var active = StayAwakeService.IsActive;
            TrayIcons.Set(open, TrayIcons.Render("IconAppWindow", "MutedBrush", px));
            TrayIcons.Set(capture, TrayIcons.Render("IconCamera", "MutedBrush", px));
            // The running keeper reads as the accent, same as the gear dot; the
            // renderer picks the text colour up from the Tag.
            TrayIcons.Set(stayAwake, TrayIcons.Render("IconBell", active ? "AccentBrush" : "MutedBrush", px));
            stayAwake.Text = active ? "Stay awake — on" : "Stay awake";
            stayAwake.Tag = active ? "accent" : null;
            TrayIcons.Set(exit, TrayIcons.Render("IconPower", "MutedBrush", px));
        }

        RefreshIcons();
        menu.Opening += (_, _) => RefreshIcons();
        return menu;
    }

    /// <summary>The embedded app icon, falling back to the generic system icon.</summary>
    static Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var sri = GetResourceStream(new Uri("pack://application:,,,/Assets/Rewake.ico"));
            if (sri != null) return new Drawing.Icon(sri.Stream);
        }
        catch { }
        return Drawing.SystemIcons.Application;
    }

    /// <summary>(Re)creates the auto-capture timer to match the current settings.</summary>
    void SetupTimer()
    {
        _timer?.Stop();
        _timer = null;
        if (!Settings.AutoCapture) return;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, Settings.IntervalMinutes))
        };
        _timer.Tick += async (_, _) => { try { await ViewModel.CaptureSilentAsync(); } catch { } };
        _timer.Start();
    }

    /// <summary>Shows (creating if needed) and activates the main window.</summary>
    void ShowMain()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new Views.MainWindow { DataContext = ViewModel };
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Maximized;
        _mainWindow.Activate();
    }

    /// <summary>Removes the tray icon and shuts the application down for real.</summary>
    public void ExitApp()
    {
        // Releases the execution-state hold; a dead process can't hold it, but
        // stopping cleanly also writes the run's summary to the log path.
        StayAwakeService.Stop();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Shutdown();
    }
}
