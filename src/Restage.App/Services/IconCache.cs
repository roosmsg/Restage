using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Restage.Models;
using Restage.Native;

namespace Restage.Services;

/// <summary>Extracts and caches application and folder icons for the UI lists.</summary>
public static class IconCache
{
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icon for an item: the folder icon for Explorer items, else the exe's.</summary>
    public static ImageSource? ForItem(WorkspaceItem item) =>
        item.Kind == ItemKind.Explorer ? GetFolderIcon() : Get(item.Exe);

    /// <summary>The executable's associated icon as a frozen 32px bitmap; null when unavailable.</summary>
    public static ImageSource? Get(string exe)
    {
        if (string.IsNullOrEmpty(exe)) return null;
        if (Cache.TryGetValue(exe, out var cached)) return cached;
        ImageSource? src = null;
        try
        {
            if (File.Exists(exe))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (ico != null)
                {
                    var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    bmp.Freeze();
                    src = bmp;
                }
            }
        }
        catch { }
        Cache[exe] = src;
        return src;
    }

    static ImageSource? _folderIcon;

    /// <summary>The same yellow folder icon Explorer shows, via the Windows shell.</summary>
    public static ImageSource? GetFolderIcon()
    {
        if (_folderIcon != null) return _folderIcon;
        try
        {
            var fi = new Win32.SHFILEINFO();
            Win32.SHGetFileInfoW("folder", Win32.FILE_ATTRIBUTE_DIRECTORY, ref fi,
                (uint)Marshal.SizeOf(fi), Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON | Win32.SHGFI_USEFILEATTRIBUTES);
            if (fi.hIcon != IntPtr.Zero)
            {
                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    fi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                bmp.Freeze();
                Win32.DestroyIcon(fi.hIcon);
                _folderIcon = bmp;
            }
        }
        catch { }
        return _folderIcon;
    }

    /// <summary>A workspace thumbnail from disk, decoded small and never cached by WPF (the file gets overwritten).</summary>
    public static ImageSource? LoadThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = 640;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}
