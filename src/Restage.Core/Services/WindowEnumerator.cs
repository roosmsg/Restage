using System.Diagnostics;
using System.Management;
using Restage.Models;
using Restage.Native;

namespace Restage.Services;

/// <summary>
/// Lists the visible top-level windows that belong to user applications, with
/// the process facts adapters need to recognise them. The full enumeration adds
/// command lines and terminal working directories (one WMI pass, about a
/// second); the light one is for polling while a restore waits for windows.
/// </summary>
public static class WindowEnumerator
{
    // System/background processes whose windows are never part of a workspace.
    // Explorer is listed because its folder windows come from the shell (see
    // ExplorerWindows), where their paths are known.
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "TextInputHost", "ApplicationFrameHost", "SystemSettings", "ShellExperienceHost",
        "SearchHost", "StartMenuExperienceHost", "LockApp", "Taskmgr", "msedgewebview2", "WWAHost",
        "PhoneExperienceHost", "Restage"
    };

    // Shells whose working directory is worth capturing so terminals reopen
    // where the user left them.
    public static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "pwsh", "cmd", "bash", "wsl", "nu", "ubuntu", "debian", "kali"
    };

    /// <summary>Visible, titled, non-cloaked top-level windows (handle, title, pid), excluding our own.</summary>
    static List<(IntPtr Handle, string Title, int Pid)> RawWindows()
    {
        var windows = new List<(IntPtr, string, int)>();
        var own = Environment.ProcessId;
        Win32.EnumWindowsProc callback = (h, _) =>
        {
            if (!Win32.IsWindowVisible(h) || Win32.IsCloaked(h) || Win32.IsToolWindow(h)) return true;
            var title = Win32.WindowText(h);
            if (title.Length == 0) return true;
            Win32.GetWindowThreadProcessId(h, out var pid);
            if ((int)pid == own) return true;
            windows.Add((h, title, (int)pid));
            return true;
        };
        Win32.EnumWindows(callback, IntPtr.Zero);
        return windows;
    }

    /// <summary>Full enumeration: command lines (WMI) and shell working directories included.</summary>
    public static List<CapturedWindow> Enumerate() => Enumerate(light: false);

    /// <summary>Cheap enumeration for polling: no WMI, no PEB reads.</summary>
    public static List<CapturedWindow> EnumerateLight() => Enumerate(light: true);

    static List<CapturedWindow> Enumerate(bool light)
    {
        var wmi = new Dictionary<int, (string? Cmd, string? ExePath)>();
        var children = new Dictionary<int, List<(int Pid, string Name, string Created)>>();
        if (!light) ReadProcessTable(wmi, children);

        var procCache = new Dictionary<int, (string Name, string Exe, string Cmd, string Args, List<string>? Cwds)?>();
        var result = new List<CapturedWindow>();
        foreach (var (handle, title, pid) in RawWindows())
        {
            try
            {
                if (!procCache.TryGetValue(pid, out var info))
                {
                    info = DescribeProcess(pid, wmi, children, light);
                    procCache[pid] = info;
                }
                if (info is not { } p || Excluded.Contains(p.Name) || p.Exe.Length == 0) continue;

                result.Add(new CapturedWindow
                {
                    Handle = handle,
                    Pid = pid,
                    Title = title,
                    Class = Win32.ClassName(handle),
                    ProcessName = p.Name,
                    ExePath = p.Exe,
                    CommandLine = p.Cmd,
                    Args = p.Args,
                    AppUserModelId = Win32.GetAppUserModelId(handle),
                    Placement = WindowPlacer.Capture(handle),
                    ShellCwds = p.Cwds,
                });
            }
            catch { }
        }
        return result;
    }

    static void ReadProcessTable(Dictionary<int, (string? Cmd, string? ExePath)> wmi,
        Dictionary<int, List<(int Pid, string Name, string Created)>> children)
    {
        // One WMI pass supplies command lines and exe paths (readable even for
        // some processes Process.MainModule can't touch) plus the parent→child
        // map used to find the shells inside terminal hosts.
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
    }

    static (string Name, string Exe, string Cmd, string Args, List<string>? Cwds)? DescribeProcess(int pid,
        Dictionary<int, (string? Cmd, string? ExePath)> wmi,
        Dictionary<int, List<(int Pid, string Name, string Created)>> children, bool light)
    {
        var name = "";
        string? exe = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            name = p.ProcessName;
        }
        catch { return null; }

        exe = Win32.ProcessImagePath(pid);
        if (string.IsNullOrEmpty(exe) && wmi.TryGetValue(pid, out var w1)) exe = w1.ExePath;
        if (string.IsNullOrEmpty(exe)) return null;

        // The PEB read is cheap and works for every process we can open; WMI
        // (full mode only) covers the elevated ones it cannot.
        var cmd = Win32.GetProcessCommandLine(pid) ?? "";
        if (cmd.Length == 0 && wmi.TryGetValue(pid, out var w2)) cmd = w2.Cmd ?? "";
        var args = cmd.Length > 0 ? StripExeFromCommandLine(cmd) : "";
        var cwds = light ? null : GetShellCwds(name, pid, children);
        return (name, exe, cmd, args, cwds);
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
                if (Win32.GetProcessCurrentDirectory(pid) is { } own && Directory.Exists(own))
                    cwds.Add(own);
            }
            else if (procName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase) &&
                     children.TryGetValue(pid, out var kids))
            {
                foreach (var kid in kids
                             .Where(k => Shells.Contains(Path.GetFileNameWithoutExtension(k.Name)))
                             .OrderBy(k => k.Created, StringComparer.Ordinal))
                {
                    if (Win32.GetProcessCurrentDirectory(kid.Pid) is { } c && Directory.Exists(c))
                        cwds.Add(c);
                }
            }
        }
        catch { }
        return cwds.Count > 0 ? cwds : null;
    }

    /// <summary>Drops the executable token (quoted or bare) from a raw command line.</summary>
    internal static string StripExeFromCommandLine(string cl)
    {
        cl = cl.TrimStart();
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
}
