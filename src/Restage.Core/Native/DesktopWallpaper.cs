using System.Runtime.InteropServices;

namespace Restage.Native;

/// <summary>
/// Reads and sets the desktop wallpaper through the documented IDesktopWallpaper
/// COM API. On Windows 11 this acts on the current virtual desktop, which is
/// exactly what a workspace wants once the user has switched to its desktop.
/// </summary>
public static class DesktopWallpaper
{
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        Win32.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(int options, uint slideshowTick);
        void GetSlideshowOptions(out int options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, int direction);
        int GetStatus();
        void Enable(bool enable);
    }

    static readonly Guid Clsid = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");

    static IDesktopWallpaper? Create()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(Clsid);
            return type == null ? null : Activator.CreateInstance(type) as IDesktopWallpaper;
        }
        catch { return null; }
    }

    /// <summary>
    /// The wallpaper in use: the shared image when every monitor shows the same
    /// one, else the first monitor's. Null when nothing is set or the API fails.
    /// </summary>
    public static string? Current()
    {
        try
        {
            if (Create() is not { } w) return null;
            var shared = SafeGet(() => w.GetWallpaper(null));
            if (!string.IsNullOrWhiteSpace(shared)) return shared;
            var count = w.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                var id = w.GetMonitorDevicePathAt(i);
                var path = SafeGet(() => w.GetWallpaper(id));
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Sets one image on every monitor. False when the file is missing or the API fails.</summary>
    public static bool Apply(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            if (Create() is not { } w) return false;
            w.SetWallpaper(null, path);
            return true;
        }
        catch { return false; }
    }

    static string? SafeGet(Func<string> read)
    {
        try { return read(); } catch { return null; }
    }
}
