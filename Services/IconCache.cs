using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Restage.Services;

/// <summary>Extracts and caches application/folder icons for the UI lists.</summary>
public static class IconCache
{
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The executable's associated icon as a frozen 32px bitmap; null when unavailable.</summary>
    public static ImageSource? Get(string exe)
    {
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
            var fi = new NativeMethods.SHFILEINFO();
            NativeMethods.SHGetFileInfo("folder",
                NativeMethods.FILE_ATTRIBUTE_DIRECTORY, ref fi,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf(fi),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON | NativeMethods.SHGFI_USEFILEATTRIBUTES);
            if (fi.hIcon != IntPtr.Zero)
            {
                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    fi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                bmp.Freeze();
                NativeMethods.DestroyIcon(fi.hIcon);
                _folderIcon = bmp;
            }
        }
        catch { }
        return _folderIcon;
    }
}
