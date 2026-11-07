using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Restage.Views;

/// <summary>
/// The tray icon's monochrome variant: the logo drawn as thin outlines in one
/// colour, white on a dark taskbar and black on a light one, like the system's
/// own outline icons. The shapes are the frame and card of Assets/Restage.svg;
/// the frame is cut back around the card so the two read apart at 16 px.
/// </summary>
internal static class TrayGlyph
{
    static readonly string[] FramePaths =
    [
        "M496 396 L603 345 L603 663 L496 712 Z",
        "M882 283 L1014 220 L1014 655 L882 717 Z",
        "M496 696 L603 649 L711 601 L751 802 Q730 815 710 804 L519 719 Q496 708 496 696 Z",
        "M496 373 Q496 348 519 332 L883 163 Q900 154 916 163 L1014 220 L1014 266 L603 413 L496 463 Z",
    ];

    const string CardPath =
        "M711 532 Q711 501 738 489 L975 367 Q997 358 1014 374 Q1030 389 1030 411 L1030 639 Q1030 667 1007 679 L745 802 Q711 819 711 784 Z";

    /// <summary>Gap between card and frame, in SVG units (the logo is about 530 wide).</summary>
    const double Gap = 40;

    /// <summary>Outline width in pixels at 16 px; it grows with the icon.</summary>
    const double StrokeAt16 = 1.0;

    /// <summary>Icon sizes Windows may ask for, from 100 % to 300 % scaling.</summary>
    static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64];

    static Geometry? _frame, _card, _all;

    /// <summary>The icon in white (dark taskbar) or black (light taskbar).</summary>
    public static Drawing.Icon Render(bool lightTaskbar)
    {
        var brush = lightTaskbar ? Brushes.Black : Brushes.White;
        var frames = Sizes.Select(px => (px, png: RenderPng(brush, px))).ToList();
        return new Drawing.Icon(new MemoryStream(BuildIco(frames)));
    }

    /// <summary>
    /// The icon with a "starting apps" badge: a still loading ring in the
    /// bottom-right corner, white or black by the taskbar, with a thin gap cut
    /// out of the icon around it so it reads at 16 px. Still on purpose (no
    /// animations); it is there while Restage launches a workspace.
    /// </summary>
    public static Drawing.Icon WithBusyBadge(Drawing.Icon icon, bool lightTaskbar)
    {
        var brush = lightTaskbar ? Brushes.Black : Brushes.White;
        var frames = Sizes.Select(px => (px, png: RenderBadged(icon, brush, px))).ToList();
        return new Drawing.Icon(new MemoryStream(BuildIco(frames)));
    }

    static byte[] RenderBadged(Drawing.Icon icon, Brush brush, int px)
    {
        using var sized = new Drawing.Icon(icon, px, px);
        var source = Imaging.CreateBitmapSourceFromHIcon(sized.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(px, px));

        var stroke = Math.Max(1.5, px / 9.0);
        var outer = px * 0.26;                       // badge radius, outer edge of the ring
        var centre = new Point(px - outer, px - outer);
        var ring = outer - stroke / 2;
        var cutout = new EllipseGeometry(centre, outer + px / 16.0, outer + px / 16.0);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var keep = Geometry.Combine(new RectangleGeometry(new Rect(0, 0, px, px)), cutout, GeometryCombineMode.Exclude, null);
            dc.PushClip(keep);
            dc.DrawImage(source, new Rect(0, 0, px, px));
            dc.Pop();

            // Three quarters of a circle, open at the top right, like a paused spinner.
            var start = new Point(centre.X, centre.Y - ring);
            var end = new Point(centre.X + ring, centre.Y);
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(start, false, false);
                g.ArcTo(end, new Size(ring, ring), 0, true, SweepDirection.Counterclockwise, true, false);
            }
            var pen = new Pen(brush, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, arc);
        }
        var target = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    static void Build()
    {
        if (_all != null) return;
        Geometry frame = Geometry.Parse(FramePaths[0]);
        foreach (var data in FramePaths.Skip(1))
            frame = Geometry.Combine(frame, Geometry.Parse(data), GeometryCombineMode.Union, null);
        var card = Geometry.Parse(CardPath);
        var halo = Geometry.Combine(card.GetWidenedPathGeometry(new Pen(Brushes.Black, 2 * Gap)), card,
            GeometryCombineMode.Union, null);
        _frame = Geometry.Combine(frame, halo, GeometryCombineMode.Exclude, null);
        _card = card;
        _all = Geometry.Combine(_frame, card, GeometryCombineMode.Union, null);
        _frame.Freeze();
        _card.Freeze();
        _all.Freeze();
    }

    static byte[] RenderPng(Brush brush, int px)
    {
        Build();
        var bounds = _all!.Bounds;
        var stroke = StrokeAt16 * px / 16;
        // Scaled to leave half an outline free on each side of the bitmap.
        var scale = (px - stroke) / Math.Max(bounds.Width, bounds.Height);
        var pen = new Pen(brush, stroke / scale) { LineJoin = PenLineJoin.Round };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform((px - bounds.Width * scale) / 2, (px - bounds.Height * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawGeometry(null, pen, _frame);
            dc.DrawGeometry(null, pen, _card);
        }
        var target = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>An .ico holding PNG frames, so Windows picks the size the tray needs.</summary>
    static byte[] BuildIco(List<(int px, byte[] png)> frames)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);            // reserved
        w.Write((ushort)1);            // type: icon
        w.Write((ushort)frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var (px, png) in frames)
        {
            w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)(px >= 256 ? 0 : px));
            w.Write((byte)0);          // palette
            w.Write((byte)0);          // reserved
            w.Write((ushort)1);        // planes
            w.Write((ushort)32);       // bits per pixel
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in frames) w.Write(png);
        w.Flush();
        return ms.ToArray();
    }
}
