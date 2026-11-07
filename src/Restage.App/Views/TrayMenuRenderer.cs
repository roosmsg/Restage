using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace Restage.Views;

/// <summary>
/// Draws the tray menu in Restage's own chrome: flat panel, 1px border, hover
/// fill — the same treatment the in-app context menus get, instead of the
/// light-grey system look that lands like a dialog from another decade.
/// Colours are read from the live theme dictionary at paint time, so the menu
/// follows a dark/light switch without being rebuilt.
/// </summary>
internal sealed class TrayMenuRenderer : WinForms.ToolStripRenderer
{
    /// <summary>Current theme colour by resource key (Dark.xaml / Light.xaml).</summary>
    internal static Drawing.Color C(string key)
    {
        var c = ((SolidColorBrush)WpfApplication.Current.Resources[key]!).Color;
        return Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    protected override void OnRenderToolStripBackground(WinForms.ToolStripRenderEventArgs e)
    {
        using var b = new Drawing.SolidBrush(C("PanelBrush"));
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(WinForms.ToolStripRenderEventArgs e)
    {
        using var p = new Drawing.Pen(C("BorderBrush2"));
        e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    // The default renderer paints a lighter gutter under the icons; one flat
    // surface instead, so the override stays empty on purpose.
    protected override void OnRenderImageMargin(WinForms.ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(WinForms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        using var b = new Drawing.SolidBrush(C("HoverBrush"));
        e.Graphics.FillRectangle(b, new Drawing.Rectangle(2, 0, e.Item.Width - 4, e.Item.Height));
    }

    // The stock layout puts the image and the text on different midlines once a
    // row has padding of its own; both are centred on the row here instead.
    protected override void OnRenderItemImage(WinForms.ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image == null) return;
        var r = e.ImageRectangle;
        var y = (e.Item.Height - r.Height) / 2;
        e.Graphics.DrawImage(e.Image, new Drawing.Rectangle(r.X, y, r.Width, r.Height));
    }

    protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
    {
        // Items opt into the accent (the running Stay Awake row) via Tag; the
        // renderer has no other channel into per-item state.
        // The shortcut column (a workspace's hotkey) comes through here too, with its own text.
        var isShortcut = e.Item is WinForms.ToolStripMenuItem { ShortcutKeyDisplayString: { Length: > 0 } s } && e.Text == s;
        e.TextColor = !e.Item.Enabled || isShortcut ? C("MutedBrush")
            : Equals(e.Item.Tag, "accent") ? C("AccentBrush")
            : C("TextBrush");
        var r = e.TextRectangle;
        e.TextRectangle = new Drawing.Rectangle(r.X, 0, r.Width, e.Item.Height);
        e.TextFormat = (e.TextFormat & ~WinForms.TextFormatFlags.Bottom) | WinForms.TextFormatFlags.VerticalCenter;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Drawing.Pen(C("BorderBrush2"));
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
    }
}

/// <summary>
/// Rasterises the app's Lucide stroke geometries (App.xaml resources) into
/// bitmaps for WinForms, so the tray menu shows the same icons as the window
/// rather than a second, slightly-off set.
/// </summary>
internal static class TrayIcons
{
    public static Drawing.Bitmap Render(string geometryKey, string brushKey, int px)
    {
        var geometry = (Geometry)WpfApplication.Current.FindResource(geometryKey);
        var color = ((SolidColorBrush)WpfApplication.Current.FindResource(brushKey)).Color;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var pen = new System.Windows.Media.Pen(new SolidColorBrush(color), 2.25)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            // 1px inset: round caps sit half a stroke outside the 24-box and
            // would clip against the bitmap edge at full scale.
            dc.PushTransform(new TranslateTransform(1, 1));
            dc.PushTransform(new ScaleTransform((px - 2.0) / 24.0, (px - 2.0) / 24.0));
            dc.DrawGeometry(null, pen, geometry);
        }

        var target = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        return new Drawing.Bitmap(ms);
    }

    /// <summary>Swap-then-dispose, so the old bitmap is never freed while shown.</summary>
    public static void Set(WinForms.ToolStripMenuItem item, Drawing.Bitmap icon)
    {
        var old = item.Image;
        item.Image = icon;
        old?.Dispose();
    }
}
