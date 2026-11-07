using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Restage.Models;
using Restage.Native;
using Restage.Services;
using Restage.ViewModels;

namespace Restage.Views;

/// <summary>
/// Main window code-behind. Bindings live in XAML; only view concerns are
/// handled here: log auto-scroll, native title-bar theming, pane sizes, focus
/// for the overlays' text boxes, and hide-to-tray.
/// </summary>
public partial class MainWindow : Window
{
    bool _logHooked;
    bool _themeHooked;
    bool _restoringLayout;
    double _bottomRowMin;

    public MainWindow()
    {
        InitializeComponent();
        RestoreLayout();
        DataContextChanged += (_, _) => { HookLogAutoScroll(); HookThemeChanges(); };
        // WPF stamps its own title-bar theme from the OS setting during load, so
        // re-apply ours at every stage that could overwrite it.
        SourceInitialized += (_, _) => { HookWindowProc(); ApplyTitleBarTheme(); };
        Loaded += (_, _) => { ApplyTitleBarTheme(); ApplyTitleIcon(); };
        DpiChanged += (_, _) => ApplyTitleIcon();
        ContentRendered += (_, _) => ApplyTitleBarTheme();
        // The settings panel closes on any click that is not on it: clicks on
        // the panel never reach this window (it is its own HWND), so every
        // mouse-down here is "outside"; so is a click on another app.
        PreviewMouseDown += (_, _) => CloseSettingsIfOpen();
        Deactivated += (_, _) => CloseSettingsIfOpen();
    }

    void CloseSettingsIfOpen()
    {
        if (SettingsPopup.IsOpen) SettingsPopup.IsOpen = false;
    }

    /// <summary>A click on the popup's transparent shadow margin, not on the panel itself.</summary>
    void SettingsPopupMargin_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, sender)) CloseSettingsIfOpen();
    }

    void HookLogAutoScroll()
    {
        if (_logHooked || DataContext is not MainViewModel vm) return;
        vm.LogEntries.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        });
        _logHooked = true;
    }

    void HookThemeChanges()
    {
        if (_themeHooked || DataContext is not MainViewModel vm) return;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDarkTheme)) ApplyTitleBarTheme();
        };
        _themeHooked = true;
    }

    // ---- Custom title bar -------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    void HookWindowProc()
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WindowProc);
    }

    /// <summary>
    /// Windows sizes a maximized resizable window to the work area plus its
    /// resize border on every side, trusting that border to hang off-screen.
    /// With the caption gone the client area covers that border too, so the
    /// content would be clipped and the taskbar covered. Answering
    /// WM_GETMINMAXINFO with the monitor's work area pins it back.
    /// </summary>
    IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024, WM_NCLBUTTONDOWN = 0x00A1, WM_NCRBUTTONDOWN = 0x00A4;
        // Title-bar clicks are non-client messages, not WPF mouse events.
        if (msg is WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN) { CloseSettingsIfOpen(); return IntPtr.Zero; }
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;
        var info = new Win32.MONITORINFOEX { Size = Marshal.SizeOf<Win32.MONITORINFOEX>() };
        if (!Win32.GetMonitorInfoW(monitor, ref info)) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        mmi.MaxSize.X = info.Work.Width;
        mmi.MaxSize.Y = info.Work.Height;
        mmi.MaxTrackSize = mmi.MaxSize;
        // Answering this message ourselves skips WPF's own answer, which is
        // where MinWidth/MinHeight are normally enforced; restate them here (in
        // physical pixels) or the window can be dragged narrower than its layout.
        var dpi = VisualTreeHelper.GetDpi(this);
        mmi.MinTrackSize.X = (int)Math.Ceiling(MinWidth * dpi.DpiScaleX);
        mmi.MinTrackSize.Y = (int)Math.Ceiling(MinHeight * dpi.DpiScaleY);
        Marshal.StructureToPtr(mmi, lParam, false);

        handled = true;
        return IntPtr.Zero;
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void MaxRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Goes through Close so the hide-to-tray rule still applies.
    void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = (DataContext as MainViewModel)?.IsDarkTheme != false ? 1 : 0;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        // Without a DWM frame (GlassFrameThickness 0) Windows 11 leaves the corners
        // square unless asked; ask, so the window looks like any other app's.
        var round = Win32.DWMWCP_ROUND;
        _ = Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    /// <summary>
    /// The title-bar icon as the .ico frame that is 1:1 at the window's DPI
    /// (16 px at 100 %, 20 at 125 %, 24 at 150 %), so it is never resampled.
    /// </summary>
    void ApplyTitleIcon()
    {
        try
        {
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var wanted = (int)Math.Round(16 * scale);
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Restage.ico"));
            if (sri == null) return;
            using var icon = new System.Drawing.Icon(sri.Stream, wanted, wanted);
            var bmp = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            TitleIcon.Source = bmp;
            TitleIcon.Width = TitleIcon.Height = icon.Width / scale;
        }
        catch { }
    }

    static AppSettings Settings => ((App)Application.Current).Settings;

    // ---- Pane sizes -------------------------------------------------------
    // The XAML mins stay the single source of truth for what's allowed — a
    // hand-edited settings file is clamped, not trusted.

    void RestoreLayout()
    {
        _bottomRowMin = BottomRow.MinHeight;
        _restoringLayout = true;
        try
        {
            var height = Settings.BottomPanelHeight;
            if (double.IsFinite(height) && height >= _bottomRowMin)
                BottomRow.Height = new GridLength(height);
            LogToggle.IsChecked = !Settings.LogCollapsed;
            ApplyLogCollapsed(Settings.LogCollapsed);
        }
        finally { _restoringLayout = false; }
    }

    // Collapsed, the bottom row shrinks to the controls bar and the seam stops
    // being draggable — there is nothing left to size.
    void ApplyLogCollapsed(bool collapsed)
    {
        LogArea.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        LogSplitter.IsEnabled = !collapsed;
        BottomRow.MinHeight = collapsed ? 0 : _bottomRowMin;
        BottomRow.Height = collapsed
            ? GridLength.Auto
            : new GridLength(Math.Max(Settings.BottomPanelHeight, _bottomRowMin));
    }

    void LogToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_restoringLayout) return;
        var collapsed = LogToggle.IsChecked != true;
        ApplyLogCollapsed(collapsed);
        Settings.LogCollapsed = collapsed;
        SettingsService.Save(Settings);
    }

    void LogSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Settings.BottomPanelHeight = BottomRow.ActualHeight;
        SettingsService.Save(Settings);
    }

    // An open Popup captures the mouse, so a click on the gear would close it
    // and then be delivered to the button, which reopens it. Taking the gear
    // out of hit-testing while the panel is up leaves that click one job: close.
    void SettingsPopup_Opened(object sender, EventArgs e) => SettingsToggle.IsHitTestVisible = false;
    void SettingsPopup_Closed(object sender, EventArgs e) => SettingsToggle.IsHitTestVisible = true;

    // ---- Overlay text boxes -------------------------------------------------
    // The box is the only thing worth typing in while its overlay is up, so it
    // takes focus with its text selected once the overlay has been arranged.

    void OverlayInput_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || sender is not TextBox box) return;
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); });
    }

    // Enter commits, rather than IsDefault on the accept button: a default button
    // answers Enter from anywhere in the window, including while its overlay is hidden.
    void ConfirmInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel vm) return;
        vm.ConfirmAcceptCommand.Execute(null);
        e.Handled = true;
    }

    void CaptureName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel vm) return;
        if (vm.CaptureSaveCommand.CanExecute(null)) vm.CaptureSaveCommand.Execute(null);
        e.Handled = true;
    }

    // Pixels moved per wheel notch. WPF's default is three "lines" — about 48px
    // — which overshoots on dense lists.
    const double WheelStep = 48;

    void Scrollable_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer((DependencyObject)sender) is not { } viewer) return;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta / 120.0 * WheelStep);
        e.Handled = true;
    }

    static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(element, i)) is { } found)
                return found;
        return null;
    }

    // View-only concern: closing hides to tray instead of exiting.
    void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (((App)Application.Current).Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
