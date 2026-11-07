using System.Runtime.InteropServices;
using System.Text;

namespace Restage.Native;

/// <summary>
/// Win32 and shell interop used by capture and restore: window enumeration and
/// placement, DWM frame bounds, monitors and DPI, AppUserModelIDs, and reading
/// another process's working directory from its PEB.
/// </summary>
internal static class Win32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public override string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    public const int SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3, SW_MINIMIZE = 6, SW_RESTORE = 9;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14, DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    public const int MDT_EFFECTIVE_DPI = 0;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLengthW(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hMonitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    static extern int DwmGetWindowAttributeRect(IntPtr hWnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder exeName, ref int size);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, PROCESS_QUERY_INFORMATION = 0x0400, PROCESS_VM_READ = 0x0010;

    /// <summary>
    /// Per-monitor DPI awareness (v2), so every rectangle this process reads or
    /// writes is in physical pixels on every monitor. A no-op once the process
    /// has a DPI context (the WPF host sets one from its manifest).
    /// </summary>
    public static void EnsurePerMonitorDpi()
    {
        try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        catch { }
    }

    public static string WindowText(IntPtr hWnd)
    {
        var len = GetWindowTextLengthW(hWnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    public static bool IsCloaked(IntPtr hWnd) =>
        DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static bool IsToolWindow(IntPtr hWnd) =>
        ((long)GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0;

    /// <summary>
    /// The window's visible frame, excluding the invisible resize border that
    /// GetWindowRect includes on Windows 10/11. Falls back to GetWindowRect.
    /// </summary>
    public static RECT FrameBounds(IntPtr hWnd)
    {
        if (DwmGetWindowAttributeRect(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<RECT>()) == 0
            && frame.Width > 0 && frame.Height > 0)
            return frame;
        GetWindowRect(hWnd, out var rect);
        return rect;
    }

    public static string? ProcessImagePath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    // ---- AppUserModelID -------------------------------------------------

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY { public Guid FormatId; public uint PropertyId; }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT
    {
        public ushort VarType;
        ushort _r1, _r2, _r3;
        public IntPtr Pointer;
        IntPtr _padding;
    }

    [DllImport("shell32.dll")]
    static extern int SHGetPropertyStoreForWindow(IntPtr hWnd, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object store);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT value);

    static readonly Guid IID_IPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    static PROPERTYKEY PKEY_AppUserModel_ID = new() { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };
    const ushort VT_LPWSTR = 31;

    /// <summary>System.AppUserModel.ID of a window, or null when the app did not set one.</summary>
    public static string? GetAppUserModelId(IntPtr hWnd)
    {
        try
        {
            var iid = IID_IPropertyStore;
            if (SHGetPropertyStoreForWindow(hWnd, ref iid, out var obj) != 0 || obj is not IPropertyStore store) return null;
            var key = PKEY_AppUserModel_ID;
            if (store.GetValue(ref key, out var value) != 0) return null;
            try
            {
                return value.VarType == VT_LPWSTR && value.Pointer != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.Pointer)
                    : null;
            }
            finally { PropVariantClear(ref value); }
        }
        catch { return null; }
    }

    // ---- Working directory of another process ----------------------------

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [DllImport("ntdll.dll")]
    static extern int NtQueryInformationProcess(IntPtr handle, int infoClass,
        ref PROCESS_BASIC_INFORMATION info, int size, out int returned);
    [DllImport("kernel32.dll")]
    static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, nint size, out nint read);

    // RTL_USER_PROCESS_PARAMETERS field offsets on x64.
    const int ProcessParametersOffset = 0x20;   // in the PEB
    const int CurrentDirectoryOffset = 0x38;    // CURDIR.DosPath (UNICODE_STRING)
    const int CommandLineOffset = 0x70;         // UNICODE_STRING

    /// <summary>
    /// Working directory of another process, read from its PEB. Null when the
    /// process is elevated or otherwise unreadable.
    /// </summary>
    public static string? GetProcessCurrentDirectory(int pid)
    {
        var path = ReadProcessParameterString(pid, CurrentDirectoryOffset);
        if (path == null) return null;
        // "C:\Users\x\" → "C:\Users\x", but keep drive roots like "C:\"
        return path.Length > 3 ? path.TrimEnd('\\') : path;
    }

    /// <summary>
    /// Command line of another process, read from its PEB: milliseconds instead
    /// of the second a WMI query costs. Null when unreadable (elevated process).
    /// </summary>
    public static string? GetProcessCommandLine(int pid) => ReadProcessParameterString(pid, CommandLineOffset);

    static string? ReadProcessParameterString(int pid, int fieldOffset)
    {
        var h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0)
                return null;
            var buf = new byte[16];
            if (!ReadProcessMemory(h, pbi.PebBaseAddress + ProcessParametersOffset, buf, 8, out _)) return null;
            var procParams = (IntPtr)BitConverter.ToInt64(buf, 0);
            if (procParams == IntPtr.Zero) return null;
            if (!ReadProcessMemory(h, procParams + fieldOffset, buf, 16, out _)) return null;
            int len = BitConverter.ToUInt16(buf, 0);
            var strPtr = (IntPtr)BitConverter.ToInt64(buf, 8);
            if (len <= 0 || len > 0x8000 || strPtr == IntPtr.Zero) return null;
            var strBuf = new byte[len];
            if (!ReadProcessMemory(h, strPtr, strBuf, len, out _)) return null;
            return Encoding.Unicode.GetString(strBuf).TrimEnd('\0');
        }
        catch { return null; }
        finally { CloseHandle(h); }
    }
}
