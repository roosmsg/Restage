using System.Windows;
using System.Windows.Interop;
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

    public MainWindow()
    {
        InitializeComponent();
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
