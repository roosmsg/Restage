using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Restage.Models;

namespace Restage.Services;

/// <summary>
/// Relaunches the applications and folders of a snapshot: one process per
/// captured window, staggered so startup spikes never overlap, with each
/// window moved back to its saved position.
/// </summary>
public static class RestoreService
{
    // Browsers restore their own windows/tabs; their args are heavily sanitized.
    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "firefox", "opera", "vivaldi"
    };

    // Apps whose startup spikes CPU/disk hard enough to deserve a longer
    // breathing gap before the next launch.
    static readonly HashSet<string> HeavyApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "firefox", "opera", "vivaldi",
        "devenv", "code", "rider64", "idea64", "studio64",
        "teams", "ms-teams", "msteams",
        "excel", "winword", "powerpnt", "photoshop", "blender"
    };

    const int LightGapMs = 700;
    const int HeavyGapMs = 2000;
    const int FolderGapMs = 300;
    const int WindowWaitMs = 4000;

    /// <summary>
    /// Launches the selected windows one at a time (staggered, so the machine
    /// never takes every startup spike at once) and reports each item's state
    /// via <paramref name="progress"/>: apps are indexed 0..apps.Count-1,
    /// folders continue at apps.Count.
    /// </summary>
    public static async Task RestoreAsync(List<AppEntry> apps, List<string> folders, bool force,
        Action<string> log, Action<int, RestoreStatus>? progress = null)
    {
        void Report(AppEntry entry, RestoreStatus status)
        {
            var i = apps.IndexOf(entry);
            if (i >= 0) progress?.Invoke(i, status);
        }

        // "Already open" must mean visible windows, not merely processes:
        // browsers keep background processes alive (startup boost) with no
        // windows, and those must not block a relaunch.
        var openWindowExes = GetExesWithVisibleWindows();

        var windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        var aliasDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps");
        var pending = new List<(Process Proc, WindowInfo Win)>();

        // One selected entry = one window to bring back; launch an instance per entry.
        foreach (var grp in apps.Where(a => !string.IsNullOrEmpty(a.Exe))
                     .GroupBy(a => a.Exe, StringComparer.OrdinalIgnoreCase))
        {
            var first = grp.First();
            var count = grp.Count();
            var suffix = count > 1 ? $" ({count} windows)" : "";
            var exe = first.Exe;

            if (exe.StartsWith(windowsApps, StringComparison.OrdinalIgnoreCase))
            {
                // Packaged (Store) apps can't be started by their exe path, but
                // most register an App Execution Alias we can launch instead.
                var alias = Path.Combine(aliasDir, Path.GetFileName(exe));
                if (Path.GetFileName(exe).Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase))
                    exe = "wt.exe";
                else if (File.Exists(alias))
                    exe = alias;
                else
                {
                    log($"SKIP  {first.Name}{suffix} — Store app, open it from the Start menu");
                    foreach (var a in grp) Report(a, RestoreStatus.Skipped);
                    continue;
                }
            }

            if (!force && openWindowExes.Contains(first.Exe))
            {
                log($"SKIP  {first.Name}{suffix} — a window is already open");
                foreach (var a in grp) Report(a, RestoreStatus.Skipped);
                continue;
            }

            var ok = 0;
            foreach (var a in grp)
            {
                Report(a, RestoreStatus.Launching);
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = BuildArgs(a),
                        UseShellExecute = true
                    };
                    if (a.Cwds?.FirstOrDefault(Directory.Exists) is { } wd)
                        psi.WorkingDirectory = wd;
                    var proc = Process.Start(psi);
                    ok++;

                    // Wait for the window and place it right away; anything the
                    // window-wait couldn't catch goes to the fallback pass below.
                    var placed = await WaitAndPlaceAsync(proc, a.Window);
                    if (!placed && proc != null && a.Window != null) pending.Add((proc, a.Window));
                    Report(a, RestoreStatus.Opened);
                }
                catch (Exception ex)
                {
                    log($"FAIL  {a.Name} — {ex.Message}");
                    Report(a, RestoreStatus.Failed);
                }
                // Breathing gap so startup spikes don't pile on top of each other.
                await Task.Delay(HeavyApps.Contains(a.Name) ? HeavyGapMs : LightGapMs);
            }
            if (ok > 0)
                log(count > 1 ? $"OK    {first.Name} — launched {ok} windows"
                              : $"OK    {first.Name}   {Trunc(first.Title)}");
        }

        // A folder already showing in an Explorer window shouldn't be opened a
        // second time — the same rule apps get above, with the same "force"
        // escape hatch. Read after the app pass so the set reflects the desktop
        // as it actually stands when the folders go out.
        var openFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!force)
            foreach (var open in SnapshotService.GetExplorerFoldersSta())
                openFolders.Add(NormalizeFolder(open));

        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < folders.Count; i++)
        {
            var f = folders[i];
            void ReportFolder(RestoreStatus s) => progress?.Invoke(apps.Count + i, s);

            if (!seenFolders.Add(NormalizeFolder(f))) { ReportFolder(RestoreStatus.Skipped); continue; }
            if (!Directory.Exists(f))
            {
                log($"SKIP  folder missing: {f}");
                ReportFolder(RestoreStatus.Skipped);
                continue;
            }
            if (openFolders.Contains(NormalizeFolder(f)))
            {
                log($"SKIP  Explorer {f} — already open");
                ReportFolder(RestoreStatus.Skipped);
                continue;
            }
            ReportFolder(RestoreStatus.Launching);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{f}\"",
                    UseShellExecute = true
                });
                log($"OK    Explorer   {f}");
                ReportFolder(RestoreStatus.Opened);
            }
            catch (Exception ex)
            {
                log($"FAIL  Explorer {f} — {ex.Message}");
                ReportFolder(RestoreStatus.Failed);
            }
            await Task.Delay(FolderGapMs);
        }

        // Fallback: windows that hadn't appeared within their own wait slot.
        if (pending.Count > 0) log("Repositioning windows…");
        var deadline = DateTime.Now.AddSeconds(15);
        while (pending.Count > 0 && DateTime.Now < deadline)
        {
            await Task.Delay(500);
            var still = new List<(Process, WindowInfo)>();
            foreach (var (proc, win) in pending)
            {
                var done = false;
                try
                {
                    if (proc.HasExited) done = true;
                    else
                    {
                        proc.Refresh();
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            Place(proc.MainWindowHandle, win);
                            done = true;
                        }
                    }
                }
                catch { done = true; }
                if (!done) still.Add((proc, win));
            }
            pending = still;
        }

        log("Restore finished. Browsers/editors reopen their own tabs if session restore is enabled in them.");
    }

    /// <summary>
    /// Polls up to 4 s for the launched process's main window, then moves it to
    /// its saved position. True when there is nothing left to do; false when the
    /// window may still appear later (caller keeps it for the fallback pass).
    /// </summary>
    static async Task<bool> WaitAndPlaceAsync(Process? proc, WindowInfo? win)
    {
        if (proc == null) return true;
        var deadline = DateTime.Now.AddMilliseconds(WindowWaitMs);
        while (DateTime.Now < deadline)
        {
            try
            {
                // Stub launchers (wt.exe, Store aliases) exit immediately and the
                // real app runs in another process — nothing to track.
                if (proc.HasExited) return true;
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    if (win != null) Place(proc.MainWindowHandle, win);
                    return true;
                }
            }
            catch { return true; }
            await Task.Delay(200);
        }
        return false;
    }

    static void Place(IntPtr hwnd, WindowInfo win)
    {
        if (win.State == "minimized") return;
        var (left, top, width, height) = ClampToScreen(win);
        NativeMethods.MoveWindow(hwnd, left, top, width, height, true);
        if (win.State == "maximized") NativeMethods.ShowWindow(hwnd, 3);
    }

    /// <summary>
    /// Saved coordinates can point at a monitor that is no longer attached
    /// (undocked laptop, changed setup). When the saved rectangle touches no
    /// current display, size and position are clamped into the work area of the
    /// nearest one so the window is always reachable.
    /// </summary>
    static (int Left, int Top, int Width, int Height) ClampToScreen(WindowInfo win)
    {
        var rect = new NativeMethods.RECT
        {
            Left = win.Left, Top = win.Top,
            Right = win.Left + win.Width, Bottom = win.Top + win.Height
        };
        if (NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero)
            return (win.Left, win.Top, win.Width, win.Height);

        var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return (win.Left, win.Top, win.Width, win.Height);

        var work = info.Work;
        var width = Math.Min(win.Width, work.Right - work.Left);
        var height = Math.Min(win.Height, work.Bottom - work.Top);
        var left = Math.Clamp(win.Left, work.Left, work.Right - width);
        var top = Math.Clamp(win.Top, work.Top, work.Bottom - height);
        return (left, top, width, height);
    }

    /// <summary>Exe paths of every process that owns a visible, titled top-level window.</summary>
    static HashSet<string> GetExesWithVisibleWindows()
    {
        var pids = new HashSet<int>();
        NativeMethods.EnumWindowsProc callback = (h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)) return true;
            NativeMethods.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out var cloaked, 4);
            if (cloaked != 0) return true;
            if (NativeMethods.GetWindowTextLength(h) == 0) return true;
            NativeMethods.GetWindowThreadProcessId(h, out var pid);
            pids.Add((int)pid);
            return true;
        };
        NativeMethods.EnumWindows(callback, IntPtr.Zero);

        var exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pid in pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.MainModule?.FileName is { } f) exes.Add(f);
            }
            catch { }
        }
        return exes;
    }

    /// <summary>
    /// Windows Terminal ignores the launcher's working directory, but its CLI
    /// takes one per tab: -d "path" for the first, "; new-tab -d" for the rest.
    /// </summary>
    static string BuildArgs(AppEntry a)
    {
        if (a.Name.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase) &&
            a.Cwds is { Count: > 0 })
        {
            // -w new: each restored entry gets its own window even when the
            // user's settings would otherwise reuse an existing one as a tab.
            var dirs = a.Cwds.Where(Directory.Exists).ToList();
            if (dirs.Count > 0)
                return "-w new " + string.Join(" ; ", dirs.Select((d, i) =>
                    i == 0 ? $"-d \"{d}\"" : $"new-tab -d \"{d}\""));
        }
        return SanitizeArgs(a.Name, a.Args);
    }

    /// <summary>
    /// Captured command lines can contain launch flags that break an interactive
    /// relaunch (Edge startup-boost runs with --no-startup-window; COM-activated
    /// apps carry -Embedding). Keep only what helps.
    /// </summary>
    internal static string SanitizeArgs(string name, string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return "";
        if (args.IndexOf("-Embedding", StringComparison.OrdinalIgnoreCase) >= 0 ||
            args.IndexOf("/Embedding", StringComparison.OrdinalIgnoreCase) >= 0 ||
            args.IndexOf("--type=", StringComparison.OrdinalIgnoreCase) >= 0)
            return "";
        if (Browsers.Contains(name))
        {
            // Keep only the profile and PWA switches; the browser's own session
            // restore brings back its windows and tabs.
            var kept = Regex.Matches(args, @"--(?:profile-directory|app|app-id)=(?:""[^""]*""|\S+)")
                .Select(m => m.Value);
            return string.Join(" ", kept);
        }
        return args;
    }

    /// <summary>
    /// Comparable form of a folder path: trailing separators dropped so
    /// "C:\Work" and "C:\Work\" match, while drive roots ("C:\") keep theirs.
    /// </summary>
    static string NormalizeFolder(string path) => path.Length > 3 ? path.TrimEnd('\\', '/') : path;

    static string Trunc(string s) => s.Length > 70 ? s[..70] + "…" : s;
}
