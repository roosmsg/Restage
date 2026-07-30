using System.Windows.Threading;
using Rewake.Models;

namespace Rewake.Services;

/// <summary>
/// Keeps the machine (and presence apps) awake by simulating a sliver of input
/// on a timer — Rewake's take on the classic "Move Mouse" utility. Runs in the
/// tray process, so closing the window does not stop it; only Stop or the
/// auto-stop deadline does.
///
/// Two belts: SendInput resets the system idle timer the same way a human
/// would (which is what Teams-style presence watches), and
/// SetThreadExecutionState tells the power manager directly that sleep is off
/// the table while we are active — either alone has gaps, together they don't.
/// </summary>
public static class StayAwakeService
{
    static DispatcherTimer? _timer;
    static AppSettings? _settings;
    static Action<string>? _log;

    public static bool IsActive => _timer != null;
    public static DateTime StartedAt { get; private set; }
    public static DateTime NextNudge { get; private set; }
    public static int NudgeCount { get; private set; }

    /// <summary>Raised on start and stop so the gear dot and tray item can follow.</summary>
    public static event Action? StateChanged;

    /// <summary>UI thread only — the timer and execution state are thread-affine.</summary>
    public static void Start(AppSettings settings, Action<string> log)
    {
        if (IsActive) return;
        _settings = settings;
        _log = log;
        StartedAt = DateTime.Now;
        NudgeCount = 0;

        NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS
            | NativeMethods.ES_SYSTEM_REQUIRED | NativeMethods.ES_DISPLAY_REQUIRED);

        var interval = TimeSpan.FromSeconds(IntervalSeconds(settings));
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        NextNudge = DateTime.Now + interval;

        var auto = settings.StayAwakeAutoStop
            ? $", stopping after {AutoStopHours(settings)} h"
            : "";
        log($"Stay awake started — every {Describe(interval)}, {MethodLabel(settings.StayAwakeMethod)}{auto}. Keeps running if the window closes.");
        StateChanged?.Invoke();
    }

    public static void Stop()
    {
        if (!IsActive) return;
        _timer!.Stop();
        _timer = null;
        // Back to normal power rules; the flag alone clears our requirements.
        NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        _log?.Invoke($"Stay awake stopped — ran {Describe(DateTime.Now - StartedAt)}, {NudgeCount} nudges.");
        StateChanged?.Invoke();
    }

    static void Tick()
    {
        var s = _settings!;

        if (s.StayAwakeAutoStop && DateTime.Now - StartedAt >= TimeSpan.FromHours(AutoStopHours(s)))
        {
            _log?.Invoke($"Stay awake auto-stop reached ({AutoStopHours(s)} h).");
            Stop();
            return;
        }

        NextNudge = DateTime.Now + _timer!.Interval;

        // The user typing or mousing already resets every idle timer there is;
        // a nudge on top would at best do nothing and at worst move their aim.
        if (s.StayAwakeOnlyWhenIdle
            && NativeMethods.GetIdleTime() < TimeSpan.FromSeconds(IntervalSeconds(s)))
            return;

        Nudge(s.StayAwakeMethod);
        NudgeCount++;
    }

    static void Nudge(string method)
    {
        switch (method)
        {
            case "Key":
                SendKeyTap(NativeMethods.VK_F15);
                break;
            case "Visible":
                // A drift the eye can catch, put back a beat later.
                SendMove(8, 8);
                _ = ReturnLater(-8, -8);
                break;
            default: // "Invisible"
                // Out and back inside one SendInput call: the idle timer resets,
                // the cursor never visibly leaves its pixel.
                SendMove(1, 1, -1, -1);
                break;
        }
    }

    static async Task ReturnLater(int dx, int dy)
    {
        await Task.Delay(150);
        SendMove(dx, dy);
    }

    /// <summary>Relative mouse moves, one INPUT per (dx, dy) pair.</summary>
    static void SendMove(params int[] deltas)
    {
        var inputs = new NativeMethods.INPUT[deltas.Length / 2];
        for (var i = 0; i < inputs.Length; i++)
            inputs[i] = new NativeMethods.INPUT
            {
                Type = NativeMethods.INPUT_MOUSE,
                Data = { Mouse = new NativeMethods.MOUSEINPUT
                {
                    Dx = deltas[i * 2],
                    Dy = deltas[i * 2 + 1],
                    Flags = NativeMethods.MOUSEEVENTF_MOVE
                } }
            };
        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    static void SendKeyTap(ushort vk)
    {
        var inputs = new NativeMethods.INPUT[]
        {
            new() { Type = NativeMethods.INPUT_KEYBOARD, Data = { Keyboard = new NativeMethods.KEYBDINPUT { Vk = vk } } },
            new() { Type = NativeMethods.INPUT_KEYBOARD, Data = { Keyboard = new NativeMethods.KEYBDINPUT { Vk = vk, Flags = NativeMethods.KEYEVENTF_KEYUP } } },
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    // Hand-edited settings files are clamped, not trusted — same rule as the
    // pane sizes in MainWindow.RestoreLayout.
    static int IntervalSeconds(AppSettings s) => Math.Clamp(s.StayAwakeIntervalSeconds, 10, 3600);
    static int AutoStopHours(AppSettings s) => Math.Clamp(s.StayAwakeAutoStopHours, 1, 24);

    static string MethodLabel(string method) => method switch
    {
        "Key" => "key press",
        "Visible" => "visible nudge",
        _ => "invisible nudge",
    };

    static string Describe(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes:D2} min"
        : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min"
        : $"{Math.Max(1, (int)t.TotalSeconds)} s";
}
