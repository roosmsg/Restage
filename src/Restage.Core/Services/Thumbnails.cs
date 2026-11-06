using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Restage.Native;

namespace Restage.Services;

/// <summary>Screenshots of the whole desktop (every monitor), scaled down for the workspace gallery.</summary>
public static class Thumbnails
{
    /// <summary>PNG of the virtual screen, at most <paramref name="maxWidth"/> pixels wide. Null on failure.</summary>
    public static byte[]? CaptureVirtualScreen(int maxWidth = 1280)
    {
        try
        {
            var x = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
            var y = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
            var w = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
            var h = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);
            if (w <= 0 || h <= 0) return null;

            using var full = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(full))
                g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);

            var scale = Math.Min(1.0, (double)maxWidth / w);
            using var small = new Bitmap((int)Math.Round(w * scale), (int)Math.Round(h * scale), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(full, 0, 0, small.Width, small.Height);
            }
            using var ms = new MemoryStream();
            small.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
