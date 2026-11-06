using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Restage.Models;

namespace Restage.Services;

/// <summary>Persists <see cref="AppSettings"/> and the HKCU Run (auto-start) entry.</summary>
public static class SettingsService
{
    /// <summary>Loads settings, falling back to defaults on any error.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.WriteAllText(Paths.SettingsFile,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Restage";

    /// <summary>True when the HKCU Run entry is present.</summary>
    public static bool GetStartWithWindows()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunValue) != null;
        }
        catch { return false; }
    }

    /// <summary>Adds or removes the HKCU Run entry (started with --minimized).</summary>
    public static void SetStartWithWindows(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --minimized");
        else k.DeleteValue(RunValue, false);
    }
}
