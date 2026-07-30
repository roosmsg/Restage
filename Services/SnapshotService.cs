using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using Rewake.Models;

namespace Rewake.Services;

/// <summary>
/// Captures the current workspace (every visible window, terminal working
/// directories, open Explorer folders) and manages the snapshot files on disk.
/// </summary>
public static class SnapshotService
{
    // System/background processes whose windows should never be captured.
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "TextInputHost", "ApplicationFrameHost", "SystemSettings", "ShellExperienceHost",
        "SearchHost", "StartMenuExperienceHost", "LockApp", "Taskmgr", "msedgewebview2", "WWAHost",
        "PhoneExperienceHost", "Rewake"
    };

    // Shells whose working directory is worth capturing so terminals can be
    // reopened where the user left them.
    static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "pwsh", "cmd", "bash", "wsl", "nu", "ubuntu", "debian", "kali"
    };

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// Stable identifier for the current Windows boot, used to detect the first
    /// run after a reboot/power cut. Falls back to tick-count arithmetic when
    /// WMI is unavailable.
    /// </summary>
    public static string GetBootId()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (ManagementObject mo in searcher.Get())
            {
                var dt = ManagementDateTimeConverter.ToDateTime(mo["LastBootUpTime"].ToString()!);
                return dt.ToString("yyyyMMddHHmmss");
            }
        }
        catch { }
        return DateTime.Now.AddMilliseconds(-Environment.TickCount64).ToString("yyyyMMddHHmmss");
    }

    /// <summary>
    /// First run after a reboot: preserves the newest pre-reboot snapshot as
    /// last-session.json before this boot's snapshots rotate it away.
    /// </summary>
    public static void PreserveLastSessionIfNewBoot()
    {
        Directory.CreateDirectory(Paths.SnapDir);
        var boot = GetBootId();
        var prev = File.Exists(Paths.Bootmark) ? File.ReadAllText(Paths.Bootmark).Trim() : "";
        if (prev == boot) return;
        var newest = new DirectoryInfo(Paths.SnapDir).GetFiles("snap-*.json")
            .OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
        if (newest != null) File.Copy(newest.FullName, Paths.LastSession, true);
        File.WriteAllText(Paths.Bootmark, boot);
    }

    /// <summary>
    /// Runs <see cref="Capture"/> on a dedicated STA thread — Shell.Application
    /// is an STA COM object, so this is safe to call from timers and background
    /// tasks.
    /// </summary>
    public static Snapshot CaptureSta()
    {
        Snapshot? result = null;
        var t = new Thread(() => result = Capture());
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        return result ?? new Snapshot { Taken = DateTime.Now.ToString("o"), Boot = GetBootId() };
    }

    /// <summary>Captures the current workspace. Must run on an STA thread.</summary>
    public static Snapshot Capture()
    {
        // One WMI pass supplies command lines/exe paths (readable even for some
        // processes Process.MainModule can't touch) and the parent→child map
        // used to find the shells inside terminal hosts.
        var wmi = new Dictionary<int, (string? Cmd, string? ExePath)>();
        var children = new Dictionary<int, List<(int Pid, string Name, string Created)>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name, CommandLine, ExecutablePath, CreationDate FROM Win32_Process");
            foreach (ManagementObject mo in searcher.Get())
            {
                var pid = Convert.ToInt32(mo["ProcessId"]);
                wmi[pid] = (mo["CommandLine"]?.ToString(), mo["ExecutablePath"]?.ToString());
                var ppid = Convert.ToInt32(mo["ParentProcessId"]);
                if (!children.TryGetValue(ppid, out var kids)) children[ppid] = kids = new();
                kids.Add((pid, mo["Name"]?.ToString() ?? "", mo["CreationDate"]?.ToString() ?? ""));
            }
        }
        catch { }

        var snap = new Snapshot { Taken = DateTime.Now.ToString("o"), Boot = GetBootId() };

        // Enumerate every visible top-level window so multiple instances of the
        // same application (e.g. several terminals) are each tracked.
        var windows = new List<(IntPtr Handle, string Title, int Pid)>();
        NativeMethods.EnumWindowsProc callback = (h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h)) return true;
            NativeMethods.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, out var cloaked, 4);
            if (cloaked != 0) return true;
            var exStyle = (long)NativeMethods.GetWindowLongPtr(h, -20 /* GWL_EXSTYLE */);
            if ((exStyle & 0x00000080L) != 0) return true; // WS_EX_TOOLWINDOW
            var len = NativeMethods.GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new System.Text.StringBuilder(len + 1);
            NativeMethods.GetWindowText(h, sb, sb.Capacity);
            NativeMethods.GetWindowThreadProcessId(h, out var pid);
            windows.Add((h, sb.ToString(), (int)pid));
            return true;
        };
        NativeMethods.EnumWindows(callback, IntPtr.Zero);

        var procCache = new Dictionary<int, (string Name, string? Exe, string Args, List<string>? Cwds)>();
        var entryPids = new List<int>(); // parallel to snap.Apps
        foreach (var w in windows)
        {
            try
            {
                if (w.Pid == Environment.ProcessId) continue;

                if (!procCache.TryGetValue(w.Pid, out var info))
                {
                    var name = ""; string? exe = null; var args = "";
                    try
                    {
                        using var p = Process.GetProcessById(w.Pid);
                        name = p.ProcessName;
                        try { exe = p.MainModule?.FileName; } catch { /* elevated process */ }
                    }
                    catch { }
                    if (exe == null && wmi.TryGetValue(w.Pid, out var w1)) exe = w1.ExePath;
                    if (wmi.TryGetValue(w.Pid, out var w2) && !string.IsNullOrEmpty(w2.Cmd))
                        args = StripExeFromCommandLine(w2.Cmd!);
                    info = (name, exe, args, GetShellCwds(name, w.Pid, children));
                    procCache[w.Pid] = info;
                }

                if (string.IsNullOrEmpty(info.Exe)) continue;
                if (Excluded.Contains(info.Name)) continue;

                NativeMethods.GetWindowRect(w.Handle, out var r);
                var state = NativeMethods.IsIconic(w.Handle) ? "minimized"
                    : NativeMethods.IsZoomed(w.Handle) ? "maximized" : "normal";

                snap.Apps.Add(new AppEntry
                {
                    Name = info.Name,
                    Exe = info.Exe!,
                    Args = info.Args,
                    Title = w.Title,
                    Window = new WindowInfo
                    {
                        Left = r.Left, Top = r.Top,
                        Width = r.Right - r.Left, Height = r.Bottom - r.Top,
                        State = state
                    }
                });
                entryPids.Add(w.Pid);
            }
            catch { }
        }

        AttachShellCwds(snap, entryPids, procCache);
        snap.Folders = GetExplorerFolders();
        return snap;
    }

    /// <summary>
    /// Working directories of the shells behind a window: the process's own cwd
    /// when it is itself a shell, or its shell children's cwds for a terminal
    /// host like Windows Terminal (one per tab, in creation order).
    /// </summary>
    static List<string>? GetShellCwds(string procName, int pid,
        Dictionary<int, List<(int Pid, string Name, string Created)>> children)
    {
        var cwds = new List<string>();
        try
        {
            if (Shells.Contains(procName))
            {
                if (NativeMethods.GetProcessCurrentDirectory(pid) is { } own && Directory.Exists(own))
                    cwds.Add(own);
            }
            else if (procName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase) &&
                     children.TryGetValue(pid, out var kids))
            {
                foreach (var kid in kids
                             .Where(k => Shells.Contains(Path.GetFileNameWithoutExtension(k.Name)))
                             .OrderBy(k => k.Created, StringComparer.Ordinal))
                {
                    if (NativeMethods.GetProcessCurrentDirectory(kid.Pid) is { } c && Directory.Exists(c))
                        cwds.Add(c);
                }
            }
        }
        catch { }
        return cwds.Count > 0 ? cwds : null;
    }

    // A terminal process can own several windows; give each window one path
    // (creation order), and when there is a single window give it every path so
    // multi-tab sessions restore as tabs. Leftover paths go to the last window.
    static void AttachShellCwds(Snapshot snap, List<int> entryPids,
        Dictionary<int, (string Name, string? Exe, string Args, List<string>? Cwds)> procCache)
    {
        var byPid = new Dictionary<int, List<AppEntry>>();
        for (var i = 0; i < snap.Apps.Count && i < entryPids.Count; i++)
        {
            if (procCache.TryGetValue(entryPids[i], out var pc) && pc.Cwds is { Count: > 0 })
            {
                if (!byPid.TryGetValue(entryPids[i], out var list)) byPid[entryPids[i]] = list = new();
                list.Add(snap.Apps[i]);
            }
        }
        foreach (var (pid, entries) in byPid)
        {
            var cwds = procCache[pid].Cwds!;
            if (entries.Count == 1) { entries[0].Cwds = new List<string>(cwds); continue; }
            for (var i = 0; i < entries.Count && i < cwds.Count; i++)
                entries[i].Cwds = i == entries.Count - 1
                    ? cwds.Skip(i).ToList()
                    : new List<string> { cwds[i] };
        }
    }

    /// <summary>Drops the executable token (quoted or bare) from a raw command line.</summary>
    static string StripExeFromCommandLine(string cl)
    {
        if (cl.StartsWith('"'))
        {
            var end = cl.IndexOf('"', 1);
            if (end > 0) return cl[(end + 1)..].Trim();
        }
        else
        {
            var sp = cl.IndexOf(' ');
            if (sp > 0) return cl[(sp + 1)..].Trim();
        }
        return "";
    }

    /// <summary>
    /// Runs <see cref="GetExplorerFolders"/> on a dedicated STA thread, so the
    /// restore path (which runs on the thread pool) can ask what Explorer
    /// currently has open. Returns empty if the shell doesn't answer in time —
    /// callers treat that as "nothing known to be open".
    /// </summary>
    public static List<string> GetExplorerFoldersSta()
    {
        List<string>? result = null;
        var t = new Thread(() => result = GetExplorerFolders());
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join(3000);
        return result ?? new List<string>();
    }

    /// <summary>Paths of every open File Explorer window, via the Shell COM API.</summary>
    static List<string> GetExplorerFolders()
    {
        var result = new List<string>();
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return result;
            dynamic shell = Activator.CreateInstance(t)!;
            foreach (var w in shell.Windows())
            {
                try
                {
                    string full = (string)w.FullName;
                    if (full.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = (string)w.Document.Folder.Self.Path;
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path) &&
                            !result.Contains(path, StringComparer.OrdinalIgnoreCase))
                            result.Add(path);
                    }
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Writes the snapshot to disk and prunes history to the newest 30 files.</summary>
    public static string SaveSnapshot(Snapshot snap)
    {
        PreserveLastSessionIfNewBoot();
        Directory.CreateDirectory(Paths.SnapDir);
        var file = Path.Combine(Paths.SnapDir, $"snap-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(snap, JsonOpts));
        // Favourites are exempt, so the cap is 30 rolling captures plus whatever
        // the user chose to keep — a starred capture is never pruned out from
        // under them.
        foreach (var old in new DirectoryInfo(Paths.SnapDir).GetFiles("snap-*.json")
                     .Where(f => !CaptureMetaService.IsFavorite(f.FullName))
                     .OrderByDescending(f => f.LastWriteTime).Skip(30))
        {
            try { old.Delete(); } catch { continue; }
            CaptureMetaService.Forget(old.FullName);
        }
        return file;
    }

    /// <summary>Reads a snapshot file; null when missing or unparsable.</summary>
    public static Snapshot? Load(string path)
    {
        try { return JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path)); }
        catch { return null; }
    }

    /// <summary>All snapshots for the sidebar: the pinned last session first, then newest to oldest.</summary>
    public static List<SnapshotFile> ListSnapshots()
    {
        var list = new List<SnapshotFile>();
        if (File.Exists(Paths.LastSession))
        {
            var s = Load(Paths.LastSession);
            if (s != null)
            {
                DateTime.TryParse(s.Taken, null, System.Globalization.DateTimeStyles.RoundtripKind, out var taken);
                list.Add(new SnapshotFile(Paths.LastSession, "⭐ Last session (pre-reboot)", taken, true,
                    s.Apps.Count, s.Folders.Count));
            }
        }
        if (Directory.Exists(Paths.SnapDir))
        {
            var captures = new List<SnapshotFile>();
            foreach (var f in new DirectoryInfo(Paths.SnapDir).GetFiles("snap-*.json")
                         .OrderByDescending(x => x.LastWriteTime))
            {
                var s = Load(f.FullName);
                if (s == null) continue;
                var meta = CaptureMetaService.Get(f.FullName);
                captures.Add(new SnapshotFile(f.FullName, f.LastWriteTime.ToString("ddd dd MMM  HH:mm:ss"),
                    f.LastWriteTime, false, s.Apps.Count, s.Folders.Count, meta?.Name, meta?.Favorite == true));
            }
            // Favourites float to the top, newest first within each half. They
            // are the ones kept on purpose, and finding one by scrolling past 30
            // rolling captures is the thing starring was meant to avoid.
            list.AddRange(captures.Where(c => c.IsFavorite));
            list.AddRange(captures.Where(c => !c.IsFavorite));
        }
        return list;
    }
}
