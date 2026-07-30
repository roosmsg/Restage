using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Rewake.Models;
using Rewake.Services;
using Rewake.ViewModels;

namespace Rewake.Views;

/// <summary>
/// Main window code-behind. Bindings live in XAML; only view concerns are
/// handled here: log auto-scroll, native title-bar theming, and hide-to-tray.
/// </summary>
public partial class MainWindow : Window
{
    bool _logHooked;
    bool _themeHooked;
    bool _restoringLayout;
    double _bottomRowMin;
    double _sidebarMin;
    double _sidebarWidth;

    public MainWindow()
    {
        InitializeComponent();
        RestoreLayout();
        DataContextChanged += (_, _) => { HookLogAutoScroll(); HookThemeChanges(); };
        // WPF on .NET 9 stamps its own title-bar theme from the OS setting during
        // load, so re-apply ours at every stage that could overwrite it.
        SourceInitialized += (_, _) => { HookWindowProc(); ApplyTitleBarTheme(); };
        Loaded += (_, _) => ApplyTitleBarTheme();
        ContentRendered += (_, _) => ApplyTitleBarTheme();
    }

    // View-only concern: keep the newest log entry visible.
    void HookLogAutoScroll()
    {
        if (_logHooked || DataContext is not MainViewModel vm) return;
        vm.LogEntries.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
        });
        _logHooked = true;
    }

    // View-only concern: match the native title bar to the app theme.
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
    /// Windows sizes a maximized resizable window to the work area *plus* its
    /// resize border on every side, trusting that border to hang off-screen.
    /// With the caption gone the client area covers that border too, so the
    /// content would be clipped and the taskbar covered. Answering
    /// WM_GETMINMAXINFO with the monitor's work area pins it back.
    /// </summary>
    IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        // Maximized position is relative to the monitor, not the desktop.
        mmi.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        mmi.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        mmi.MaxSize.X = info.Work.Right - info.Work.Left;
        mmi.MaxSize.Y = info.Work.Bottom - info.Work.Top;
        mmi.MaxTrackSize = mmi.MaxSize;
        Marshal.StructureToPtr(mmi, lParam, false);

        handled = true;
        return IntPtr.Zero;
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void MaxRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // Goes through Close so the hide-to-tray rule still applies.
    void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var dark = (DataContext as MainViewModel)?.IsDarkTheme != false ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    static AppSettings Settings => ((App)Application.Current).Settings;

    // View-only concern: pane sizes. The XAML mins/maxes stay the single source
    // of truth for what's allowed — a hand-edited settings file is clamped, not
    // trusted.
    void RestoreLayout()
    {
        _bottomRowMin = BottomRow.MinHeight;
        _sidebarMin = SidebarColumn.MinWidth;
        _sidebarWidth = SidebarColumn.Width.Value;
        _restoringLayout = true;
        try
        {
            var width = Settings.SidebarWidth;
            if (double.IsFinite(width))
                _sidebarWidth = Math.Clamp(width, _sidebarMin, SidebarColumn.MaxWidth);

            var height = Settings.BottomPanelHeight;
            if (double.IsFinite(height) && height >= _bottomRowMin)
                BottomRow.Height = new GridLength(height);

            // The toggle reads as "sidebar showing", so it is the inverse of the
            // stored flag — a lit-up dock glyph next to a hidden pane would lie.
            SidebarToggle.IsChecked = !Settings.SidebarCollapsed;
            ApplySidebarCollapsed(Settings.SidebarCollapsed);

            LogToggle.IsChecked = Settings.LogCollapsed;
            ApplyLogCollapsed(Settings.LogCollapsed);
        }
        finally { _restoringLayout = false; }
    }

    // Hiding the sidebar means surrendering the column's MinWidth too: a
    // minimum of 208 would keep the pane on screen whatever width we set. The
    // seam goes with it — with nothing to its left there is nothing to size,
    // and an invisible drag strip against the window edge reads as a bug.
    void ApplySidebarCollapsed(bool collapsed)
    {
        SidebarPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarSplitter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarColumn.MinWidth = collapsed ? 0 : _sidebarMin;
        SidebarColumn.Width = new GridLength(collapsed ? 0 : _sidebarWidth);
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

    // Checked/Unchecked rather than Click: Click only fires for a literal mouse
    // press, so anything that sets IsChecked another way — automation, a future
    // binding — would leave the panel and the flag disagreeing.
    void LogToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_restoringLayout) return;
        var collapsed = LogToggle.IsChecked == true;
        ApplyLogCollapsed(collapsed);
        Settings.LogCollapsed = collapsed;
        SettingsService.Save(Settings);
    }

    void SidebarToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_restoringLayout) return;
        var collapsed = SidebarToggle.IsChecked != true;
        ApplySidebarCollapsed(collapsed);
        Settings.SidebarCollapsed = collapsed;
        SettingsService.Save(Settings);
    }

    // An open Popup captures the mouse, so a click on the gear would close it
    // and then be delivered to the button, which reopens it — the panel could
    // never be dismissed from the control that opened it. Taking the gear out
    // of hit-testing while the panel is up leaves that click one job: close.
    void SettingsPopup_Opened(object sender, EventArgs e) => SettingsToggle.IsHitTestVisible = false;

    void SettingsPopup_Closed(object sender, EventArgs e) => SettingsToggle.IsHitTestVisible = true;

    // Persist on drag end rather than on close: the window hides to tray far
    // more often than it actually closes. Each seam saves only its own
    // dimension — a narrow window can squeeze the sidebar below its stored
    // width, and dragging the log must not write that squeeze back as intent.
    void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _sidebarWidth = SidebarColumn.ActualWidth;
        Settings.SidebarWidth = _sidebarWidth;
        SettingsService.Save(Settings);
    }

    void LogSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Settings.BottomPanelHeight = BottomRow.ActualHeight;
        SettingsService.Save(Settings);
    }

    // The prompt's box is the only thing worth typing in while it is up, so it
    // takes focus with its text selected — typing replaces the old name.
    void ConfirmInput_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        // The TextBox is still mid-layout when Visibility changes; focus lands
        // once the overlay has actually been arranged.
        Dispatcher.BeginInvoke(() => { ConfirmInputBox.Focus(); ConfirmInputBox.SelectAll(); });
    }

    // Enter commits, rather than IsDefault on the accept button: a default button
    // answers Enter from anywhere in the window, including while the overlay that
    // owns it is hidden.
    void ConfirmInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel vm) return;
        vm.ConfirmAcceptCommand.Execute(null);
        e.Handled = true;
    }

    // Pixels moved per wheel notch. WPF's default is three "lines" — about 48px
    // — which overshoots on lists this dense. The two ListBoxes set
    // CanContentScroll="False" so their offsets are pixels too, not item counts.
    const double WheelStep = 32;

    void Scrollable_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer((DependencyObject)sender) is not { } viewer) return;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta / 120.0 * WheelStep);
        e.Handled = true;
    }

    // The ScrollViewer of a ListBox lives inside its template, so the handler
    // has to go looking for it; for a bare ScrollViewer this returns at once.
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
