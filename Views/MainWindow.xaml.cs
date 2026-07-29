using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
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

    public MainWindow()
    {
        InitializeComponent();
        RestoreLayout();
        DataContextChanged += (_, _) => { HookLogAutoScroll(); HookThemeChanges(); };
        // WPF on .NET 9 stamps its own title-bar theme from the OS setting during
        // load, so re-apply ours at every stage that could overwrite it.
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
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
        _restoringLayout = true;
        try
        {
            var width = Settings.SidebarWidth;
            if (double.IsFinite(width))
                SidebarColumn.Width = new GridLength(
                    Math.Clamp(width, SidebarColumn.MinWidth, SidebarColumn.MaxWidth));

            var height = Settings.BottomPanelHeight;
            if (double.IsFinite(height) && height >= _bottomRowMin)
                BottomRow.Height = new GridLength(height);

            LogToggle.IsChecked = Settings.LogCollapsed;
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

    // Persist on drag end rather than on close: the window hides to tray far
    // more often than it actually closes. Each seam saves only its own
    // dimension — a narrow window can squeeze the sidebar below its stored
    // width, and dragging the log must not write that squeeze back as intent.
    void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Settings.SidebarWidth = SidebarColumn.ActualWidth;
        SettingsService.Save(Settings);
    }

    void LogSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Settings.BottomPanelHeight = BottomRow.ActualHeight;
        SettingsService.Save(Settings);
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
