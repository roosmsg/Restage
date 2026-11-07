using System.Windows.Interop;
using Restage.Models;
using Restage.Native;

namespace Restage.Services;

/// <summary>
/// Global hotkeys through RegisterHotKey on a message-only window of the UI
/// thread, so they work while the main window is closed and the app lives in
/// the tray. Windows refuses a combination another app (or the shell) holds;
/// <see cref="Register"/> reports that so the UI can say so.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    static readonly IntPtr HwndMessage = new(-3);

    readonly HwndSource _source;
    readonly Dictionary<int, string> _actions = new();
    int _nextId = 1;

    /// <summary>Raised on the UI thread with the action of the hotkey that was pressed.</summary>
    public event Action<string>? Pressed;

    public HotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("Restage hotkeys") { ParentWindow = HwndMessage, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <summary>Registers one hotkey for an action; false when Windows refuses it (taken elsewhere).</summary>
    public bool Register(string action, Hotkey hotkey)
    {
        var id = _nextId++;
        if (!Win32.RegisterHotKey(_source.Handle, id, (uint)hotkey.Modifiers | Win32.MOD_NOREPEAT, (uint)hotkey.Key))
            return false;
        _actions[id] = action;
        return true;
    }

    /// <summary>True when Windows would give this combination to Restage right now (a trial registration).</summary>
    public bool IsAvailable(Hotkey hotkey)
    {
        const int TrialId = 0xBFFF; // top of the range RegisterHotKey allows applications
        if (!Win32.RegisterHotKey(_source.Handle, TrialId, (uint)hotkey.Modifiers | Win32.MOD_NOREPEAT, (uint)hotkey.Key))
            return false;
        Win32.UnregisterHotKey(_source.Handle, TrialId);
        return true;
    }

    /// <summary>Releases every hotkey, e.g. while the user records a new one.</summary>
    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Win32.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
