using Restage.Models;
using Xunit;

namespace Restage.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+R", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x52)]
    [InlineData("ctrl+shift+1", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x31)]
    [InlineData("Control+Windows+F12", HotkeyModifiers.Ctrl | HotkeyModifiers.Win, 0x7B)]
    [InlineData("Alt+Num7", HotkeyModifiers.Alt, 0x67)]
    [InlineData("Win+Space", HotkeyModifiers.Win, 0x20)]
    [InlineData("Ctrl+Alt+PageDown", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x22)]
    [InlineData("Ctrl+Alt+/", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0xBF)]
    [InlineData("Ctrl+=", HotkeyModifiers.Ctrl, 0xBB)]
    [InlineData(" Ctrl + Alt + r ", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x52)]
    public void Parses_text_form(string text, HotkeyModifiers mods, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(mods, vk), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("R")]                 // no modifier
    [InlineData("Shift+R")]           // Shift alone would eat capitals
    [InlineData("Ctrl+Alt")]          // no key
    [InlineData("Ctrl+R+T")]          // two keys
    [InlineData("Ctrl++")]            // "+" separates; the plus key is "="
    [InlineData("Ctrl+Banana")]       // unknown key
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Num10")]
    public void Refuses_unusable_combinations(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Theory]
    [InlineData("Ctrl+Alt+R")]
    [InlineData("Ctrl+Shift+Win+F5")]
    [InlineData("Alt+Num0")]
    [InlineData("Ctrl+Alt+'")]
    [InlineData("Win+Delete")]
    [InlineData("Ctrl+Alt+NumAdd")]
    public void Round_trips_in_canonical_order(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(text, hotkey.ToString());
    }

    [Fact]
    public void Writes_modifiers_in_a_fixed_order()
    {
        Assert.True(Hotkey.TryParse("Win+Shift+Alt+Ctrl+K", out var hotkey));
        Assert.Equal("Ctrl+Alt+Shift+Win+K", hotkey.ToString());
    }

    [Fact]
    public void Workspace_actions_carry_the_id()
    {
        var action = HotkeyActions.Workspace("abc123");
        Assert.Equal("abc123", HotkeyActions.WorkspaceId(action));
        Assert.Null(HotkeyActions.WorkspaceId("show"));
    }
}
