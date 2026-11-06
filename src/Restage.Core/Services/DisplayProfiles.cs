using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Restage.Models;
using Restage.Native;

namespace Restage.Services;

/// <summary>Detects the current monitor arrangement and gives it a stable id and a readable name.</summary>
public static class DisplayProfiles
{
    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        Win32.MonitorEnumProc callback = (IntPtr hMon, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFOEX { Size = Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (!Win32.GetMonitorInfoW(hMon, ref info)) return true;
            uint dpi = 96;
            if (Win32.GetDpiForMonitor(hMon, Win32.MDT_EFFECTIVE_DPI, out var dx, out _) == 0) dpi = dx;
            list.Add(new MonitorInfo
            {
                Device = info.Device,
                Left = info.Monitor.Left, Top = info.Monitor.Top,
                Width = info.Monitor.Width, Height = info.Monitor.Height,
                WorkLeft = info.Work.Left, WorkTop = info.Work.Top,
                WorkWidth = info.Work.Width, WorkHeight = info.Work.Height,
                Dpi = dpi,
                IsPrimary = (info.Flags & Win32.MONITORINFOF_PRIMARY) != 0,
            });
            return true;
        };
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return list.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
    }

    public static DisplayProfile Current() => From(Monitors());

    public static DisplayProfile From(List<MonitorInfo> monitors)
    {
        var descriptor = string.Join(";", monitors
            .OrderBy(m => m.Device, StringComparer.Ordinal)
            .Select(m => $"{m.Device}|{m.Left},{m.Top},{m.Width},{m.Height}|{m.Dpi}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(descriptor));
        return new DisplayProfile
        {
            Id = Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant(),
            Name = Describe(monitors),
            Monitors = monitors,
        };
    }

    /// <summary>"1 display · 2560×1440" or "2 displays · 3840×2160 + 1920×1080".</summary>
    public static string Describe(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return "No displays";
        var sizes = string.Join(" + ", monitors.Select(m => $"{m.Width}×{m.Height}"));
        return $"{monitors.Count} display{(monitors.Count == 1 ? "" : "s")} · {sizes}";
    }
}
