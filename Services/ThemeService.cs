using System.Windows;

namespace Restage.Services;

/// <summary>Swaps the application's theme ResourceDictionary at runtime.</summary>
public static class ThemeService
{
    /// <summary>Applies "Light" or "Dark" (anything else falls back to Dark).</summary>
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
    }
}
