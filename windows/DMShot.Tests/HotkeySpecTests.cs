using DMShot.Platform;
using Xunit;

public class HotkeySpecTests
{
    [Fact]
    public void Parse_CtrlShiftDigit()
    {
        var s = HotkeySpec.Parse("Ctrl+Shift+1");
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, s.Modifiers);
        Assert.Equal((uint)0x31, s.VirtualKey); // VK '1'
    }

    [Fact]
    public void Format_RoundTrips()
    {
        var s = new HotkeySpec(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x32);
        Assert.Equal("Ctrl+Shift+2", s.Format());
        Assert.Equal(s, HotkeySpec.Parse(s.Format()));
    }

    [Fact]
    public void Parse_LetterKey()
    {
        var s = HotkeySpec.Parse("Alt+A");
        Assert.Equal(HotkeyModifiers.Alt, s.Modifiers);
        Assert.Equal((uint)0x41, s.VirtualKey);
    }
}
