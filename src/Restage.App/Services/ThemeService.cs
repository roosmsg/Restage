using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace Restage.Services;

/// <summary>
/// Swaps the application's theme ResourceDictionary at runtime and paints the
/// accent in the user's Windows accent colour, following it when it changes.
/// </summary>
public static class ThemeService
{
    static bool _hooked;

    /// <summary>Applies "Light" or "Dark" (anything else falls back to Dark), then the Windows accent.</summary>
    public static void Apply(string? theme)
    {
        var name = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{name}.xaml")
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
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                    Application.Current?.Dispatcher.BeginInvoke(ApplyAccent);
            };
            _hooked = true;
        }
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
