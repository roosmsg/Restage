using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace Restage.Services;

/// <summary>
/// Swaps the application's theme ResourceDictionary at runtime, follows the
/// Windows light/dark setting when asked to, and paints the accent in the
/// user's Windows accent colour, following it when it changes.
/// </summary>
public static class ThemeService
{
    public const string System = "System", Dark = "Dark", Light = "Light";

    static bool _hooked;
    static string _theme = System;

    /// <summary>True while the dark dictionary is in use (after resolving "System").</summary>
    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised after a theme was applied, including a system-driven switch.</summary>
    public static event Action? Applied;

    /// <summary>Applies "System", "Light" or "Dark" (anything else counts as System), then the Windows accent.</summary>
    public static void Apply(string? theme)
    {
        _theme = theme is Light or Dark ? theme : System;
        IsDark = ResolveDark(_theme);
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(IsDark ? Dark : Light)}.xaml")
        };
        var dicts = Application.Current.Resources.MergedDictionaries;
        var old = dicts.FirstOrDefault(d =>
            d.Source != null && d.Source.OriginalString.Contains("/Themes/"));
        if (old != null) dicts.Remove(old);
        dicts.Add(dict);

        ApplyAccent();
        if (!_hooked)
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    // A light/dark flip in Windows re-applies the whole theme;
                    // anything else (accent colour) only repaints the accent.
                    if (_theme == System && ResolveDark(System) != IsDark) Apply(System);
                    else ApplyAccent();
                });
            };
            _hooked = true;
        }
        Applied?.Invoke();
    }

    static bool ResolveDark(string theme) => theme switch
    {
        Light => false,
        Dark => true,
        _ => !SystemUsesLightTheme(),
    };

    /// <summary>Windows' "Choose your default app mode" setting (Settings › Personalisation › Colours).</summary>
    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Windows' "Choose your default Windows mode" setting: the taskbar's and
    /// notification area's light or dark look, which can differ from the apps'.
    /// </summary>
    public static bool TaskbarUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Overrides the theme's accent brushes with the Windows accent colour.
    /// Entries set directly on the application's resources win over the merged
    /// theme dictionary, so the theme files keep their fallback green for a
    /// system that has no accent colour to read.
    /// </summary>
    public static void ApplyAccent()
    {
        var resources = Application.Current.Resources;
        if (ReadWindowsAccent() is not { } accent)
        {
            resources.Remove("AccentBrush");
            resources.Remove("AccentFillBrush");
            resources.Remove("AccentHoverBrush");
            resources.Remove("AccentTextBrush");
            return;
        }
        // A touch lighter than Windows paints it for text, check marks, progress
        // and outlines; below it for button fills, which read too bright against
        // the dark panels at full strength.
        var fill = Scale(accent, 0.62);
        resources["AccentBrush"] = Frozen(Lighten(accent, 0.18));
        resources["AccentFillBrush"] = Frozen(fill);
        resources["AccentHoverBrush"] = Frozen(Scale(accent, 0.52));
        resources["AccentTextBrush"] = Frozen(Luminance(fill) > 0.62 ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White);
    }

    /// <summary>The accent colour Windows paints its own chrome with (Settings › Personalisation › Colours).</summary>
    public static Color? ReadWindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int raw)
            {
                var v = unchecked((uint)raw); // 0xAABBGGRR
                return Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
            }
        }
        catch { }
        return null;
    }

    static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>Mixes the colour toward white by the given fraction (0 = unchanged, 1 = white).</summary>
    static Color Lighten(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    static Color Scale(Color c, double f) =>
        Color.FromRgb((byte)Math.Clamp(c.R * f, 0, 255), (byte)Math.Clamp(c.G * f, 0, 255), (byte)Math.Clamp(c.B * f, 0, 255));

    static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}
