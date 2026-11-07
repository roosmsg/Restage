namespace Restage.Models;

/// <summary>Modifier keys of a global hotkey; the values are RegisterHotKey's MOD_* flags.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A global key combination such as Ctrl+Alt+R: modifiers plus one Windows
/// virtual-key code. Stored in app-settings.json as its text form, which is
/// also what the UI shows.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int Key)
{
    /// <summary>
    /// Usable as a global hotkey: a non-modifier key with Ctrl, Alt or Win.
    /// Shift alone is refused, or Shift+letter would swallow capitals in every app.
    /// </summary>
    public bool IsValid =>
        KeyName(Key) != null
        && (Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    public override string ToString()
    {
        var key = KeyName(Key) ?? $"0x{Key:X2}";
        return Modifiers == HotkeyModifiers.None ? key : $"{ModifierText(Modifiers)}+{key}";
    }

    /// <summary>Modifiers in the fixed order Windows uses: "Ctrl+Alt+Shift+Win".</summary>
    public static string ModifierText(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        return string.Join("+", parts);
    }

    /// <summary>Parses the text form ("Ctrl+Alt+R", case-insensitive); false for anything not <see cref="IsValid"/>.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = HotkeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            // The plus key is written "=", its unshifted label, so "+" only separates.
            if (part.Length == 0) return false;
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": mods |= HotkeyModifiers.Win; continue;
            }
            if (key != null) return false;
            key = KeyCode(part);
            if (key == null) return false;
        }
        if (key == null) return false;
        hotkey = new Hotkey(mods, key.Value);
        return hotkey.IsValid;
    }

    static readonly Dictionary<int, string> Named = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x1B] = "Esc",
        [0x20] = "Space", [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2C] = "PrintScreen", [0x2D] = "Insert", [0x2E] = "Delete",
        // Spelled out: "Num+" would read as a separator in the text form.
        [0x6A] = "NumMultiply", [0x6B] = "NumAdd", [0x6D] = "NumSubtract", [0x6E] = "NumDecimal", [0x6F] = "NumDivide",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
        [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    /// <summary>Display name of a virtual-key code, or null when it cannot be a hotkey key.</summary>
    public static string? KeyName(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x60 and <= 0x69 => $"Num{vk - 0x60}",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        _ => Named.GetValueOrDefault(vk),
    };

    static int? KeyCode(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length > 3 && name.StartsWith("Num", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(3), out var n) && n is >= 0 and <= 9)
            return 0x60 + n;
        if (name.Length > 1 && (name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return 0x6F + f;
        foreach (var (vk, label) in Named)
            if (label.Equals(name, StringComparison.OrdinalIgnoreCase)) return vk;
        return null;
    }
}
