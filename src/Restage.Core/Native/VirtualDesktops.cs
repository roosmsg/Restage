using System.Runtime.InteropServices;
using Microsoft.Win32;
using Restage.Models;

namespace Restage.Native;

/// <summary>
/// Windows 11 virtual desktops through documented means only: the public
/// IVirtualDesktopManager (which desktop is a window on, move our own window)
/// and the registry keys Explorer maintains (ids, order, names, the current
/// desktop). Switching desktops has no public API; the user does that, and
/// <see cref="WaitUntilCurrentAsync"/> notices when they have.
/// </summary>
public static class VirtualDesktops
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hWnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hWnd, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hWnd, ref Guid desktopId);
    }

    static readonly Guid ManagerClsid = new("aa509086-5ca9-4c25-8f95-589d3c07b48a");

    static IVirtualDesktopManager? Manager()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(ManagerClsid);
            return type == null ? null : Activator.CreateInstance(type) as IVirtualDesktopManager;
        }
        catch { return null; }
    }

    /// <summary>Desktop ids in Task View order; empty when the key is missing (no desktops created yet).</summary>
    public static IReadOnlyList<Guid> Ids()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            if (k?.GetValue("VirtualDesktopIDs") is byte[] bytes && bytes.Length >= 16)
            {
                var ids = new List<Guid>(bytes.Length / 16);
                for (var i = 0; i + 16 <= bytes.Length; i += 16)
                    ids.Add(new Guid(bytes.AsSpan(i, 16)));
                return ids;
            }
        }
        catch { }
        return Array.Empty<Guid>();
    }

    /// <summary>Id of the desktop the user is looking at, or null when it cannot be determined.</summary>
    public static Guid? CurrentId()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            if (k?.GetValue("CurrentVirtualDesktop") is byte[] bytes && bytes.Length == 16)
                return new Guid(bytes);
        }
        catch { }
        // Fallback: whatever desktop the foreground window is on.
        var fg = Win32.GetForegroundWindow();
        if (fg != IntPtr.Zero && Manager() is { } m && m.GetWindowDesktopId(fg, out var id) == 0 && id != Guid.Empty)
            return id;
        var ids = Ids();
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>The user-given name of a desktop, or null when it still has its default name.</summary>
    public static string? Name(Guid id)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"{Key}\Desktops\{id:B}");
            return k?.GetValue("Name") as string is { Length: > 0 } s ? s : null;
        }
        catch { return null; }
    }

    public static DesktopRef Describe(Guid id)
    {
        var ids = Ids();
        var index = ids.ToList().IndexOf(id);
        return new DesktopRef { Id = id, Name = Name(id), Index = index >= 0 ? index : null };
    }

    /// <summary>The current desktop, or null on a system without virtual desktop state.</summary>
    public static DesktopRef? Current() => CurrentId() is { } id ? Describe(id) : null;

    /// <summary>
    /// Finds the live desktop a saved reference points at: by id first, then by
    /// name (ids change when a desktop is deleted and recreated). Null when it
    /// no longer exists.
    /// </summary>
    public static DesktopRef? Resolve(DesktopRef saved)
    {
        var ids = Ids();
        if (saved.Id is { } id && ids.Contains(id)) return Describe(id);
        if (!string.IsNullOrWhiteSpace(saved.Name))
            foreach (var candidate in ids)
                if (string.Equals(Name(candidate), saved.Name, StringComparison.OrdinalIgnoreCase))
                    return Describe(candidate);
        return null;
    }

    public static bool IsCurrent(Guid id) => CurrentId() == id;

    /// <summary>True when the window is on the desktop the user is looking at.</summary>
    public static bool IsWindowOnCurrentDesktop(IntPtr hWnd) =>
        Manager() is { } m && m.IsWindowOnCurrentVirtualDesktop(hWnd, out var on) == 0 && on != 0;

    /// <summary>Moves a window of this process to a desktop (the public API only allows our own windows).</summary>
    public static bool MoveWindowToDesktop(IntPtr hWnd, Guid id) =>
        Manager() is { } m && m.MoveWindowToDesktop(hWnd, ref id) == 0;

    /// <summary>Polls until the user has switched to the desktop, the timeout passes, or the token cancels.</summary>
    public static async Task<bool> WaitUntilCurrentAsync(Guid id, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsCurrent(id)) return true;
            try { await Task.Delay(250, ct); } catch (TaskCanceledException) { return false; }
        }
        return IsCurrent(id);
    }
}
