using System.Runtime.InteropServices;
using Restage.Models;
using Restage.Native;

namespace Restage.Services;

/// <summary>
/// Reads and applies window placements without drift. Capturing uses the DWM
/// frame bounds, which exclude the invisible resize border that GetWindowRect
/// includes on Windows 10/11; applying measures that border on the live window
/// and compensates, so a window saved and restored lands on the same pixels.
/// </summary>
public static class WindowPlacer
{
    public static WindowPlacement Capture(IntPtr hWnd)
    {
        var state = Win32.IsIconic(hWnd) ? WindowShowState.Minimized
            : Win32.IsZoomed(hWnd) ? WindowShowState.Maximized
            : WindowShowState.Normal;

        Win32.RECT rect;
        if (state == WindowShowState.Normal)
        {
            rect = Win32.FrameBounds(hWnd);
        }
        else
        {
            // The restored rectangle Windows keeps for the window; SetWindowPlacement
            // takes the same coordinates back, so no border arithmetic is needed.
            var wp = new Win32.WINDOWPLACEMENT { Length = Marshal.SizeOf<Win32.WINDOWPLACEMENT>() };
            rect = Win32.GetWindowPlacement(hWnd, ref wp) ? wp.NormalPosition : Win32.FrameBounds(hWnd);
        }

        return new WindowPlacement
        {
            Left = rect.Left, Top = rect.Top, Width = rect.Width, Height = rect.Height,
            State = state,
            Monitor = MonitorDevice(hWnd),
        };
    }

    /// <summary>Moves a window to a saved placement; clamps to the nearest monitor when its own is gone.</summary>
    public static bool Apply(IntPtr hWnd, WindowPlacement p)
    {
        if (hWnd == IntPtr.Zero || !Win32.IsWindow(hWnd)) return false;
        try
        {
            if (p.State != WindowShowState.Normal)
            {
                var wp = new Win32.WINDOWPLACEMENT
                {
                    Length = Marshal.SizeOf<Win32.WINDOWPLACEMENT>(),
                    ShowCmd = p.State == WindowShowState.Minimized ? Win32.SW_SHOWMINIMIZED : Win32.SW_SHOWMAXIMIZED,
                    NormalPosition = new Win32.RECT { Left = p.Left, Top = p.Top, Right = p.Right, Bottom = p.Bottom },
                };
                return Win32.SetWindowPlacement(hWnd, ref wp);
            }

            if (Win32.IsIconic(hWnd) || Win32.IsZoomed(hWnd)) Win32.ShowWindow(hWnd, Win32.SW_RESTORE);

            var target = ClampToMonitors(new Win32.RECT { Left = p.Left, Top = p.Top, Right = p.Right, Bottom = p.Bottom });

            // Border compensation: GetWindowRect is what SetWindowPos sets, the
            // frame is what the user sees. Their difference is the invisible
            // border, constant per window, so add it back on each side.
            Win32.GetWindowRect(hWnd, out var window);
            var frame = Win32.FrameBounds(hWnd);
            var x = target.Left + (window.Left - frame.Left);
            var y = target.Top + (window.Top - frame.Top);
            var cx = target.Width + (window.Width - frame.Width);
            var cy = target.Height + (window.Height - frame.Height);
            return Win32.SetWindowPos(hWnd, IntPtr.Zero, x, y, Math.Max(cx, 1), Math.Max(cy, 1),
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
        catch { return false; }
    }

    /// <summary>
    /// Saved coordinates can point at a monitor that is no longer attached. When
    /// the rectangle touches no current display, it is sized and moved into the
    /// work area of the nearest one so the window is always reachable.
    /// </summary>
    static Win32.RECT ClampToMonitors(Win32.RECT rect)
    {
        if (Win32.MonitorFromRect(ref rect, Win32.MONITOR_DEFAULTTONULL) != IntPtr.Zero) return rect;
        var monitor = Win32.MonitorFromRect(ref rect, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFOEX { Size = Marshal.SizeOf<Win32.MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !Win32.GetMonitorInfoW(monitor, ref info)) return rect;

        var work = info.Work;
        var width = Math.Min(rect.Width, work.Width);
        var height = Math.Min(rect.Height, work.Height);
        var left = Math.Clamp(rect.Left, work.Left, work.Right - width);
        var top = Math.Clamp(rect.Top, work.Top, work.Bottom - height);
        return new Win32.RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    static string? MonitorDevice(IntPtr hWnd)
    {
        var monitor = Win32.MonitorFromWindow(hWnd, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFOEX { Size = Marshal.SizeOf<Win32.MONITORINFOEX>() };
        return monitor != IntPtr.Zero && Win32.GetMonitorInfoW(monitor, ref info) ? info.Device : null;
    }
}
