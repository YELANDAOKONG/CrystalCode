using CrystalCode.Display.Input;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Input;

public sealed class InputDecoderTests
{
    [Fact]
    public void Push_TabAndEnter_MatchOnWindowsLinuxAndMac()
    {
        var decoder = new InputDecoder();
        AssertTab(decoder.Push(Linux(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false))));
        AssertTab(decoder.Push(WindowsVt("\t")));
        AssertTab(decoder.Push(Mac(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false))));

        AssertEnter(decoder.Push(Linux(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false))));
        AssertEnter(decoder.Push(WindowsVt("\r")));
        AssertEnter(decoder.Push(WindowsVt("\r\n")));
        AssertEnter(decoder.Push(Mac(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false))));
    }

    [Fact]
    public void Push_LetterY_MatchesOnEveryPlatform()
    {
        var decoder = new InputDecoder();
        AssertKey(decoder.Push(Linux(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false))), ConsoleKey.Y, 'y');
        AssertKey(decoder.Push(WindowsVt("y")), ConsoleKey.Y, 'y');
        AssertKey(decoder.Push(Mac(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false))), ConsoleKey.Y, 'y');
    }

    [Fact]
    public void Push_BracketedPaste_WindowsVtAndUnix()
    {
        var payload = "\u001b[200~hello\r\nworld\u001b[201~";
        var unix = UnixCsi(payload);
        var windows = WindowsVt(payload);

        AssertPaste(new InputDecoder().Push(unix), "hello\nworld");
        AssertPaste(new InputDecoder().Push(windows), "hello\nworld");
    }

    [Fact]
    public void Push_SgrWheel_WindowsVtAndUnix()
    {
        var payload = "\u001b[<64;12;8M";
        AssertWheel(new InputDecoder().Push(UnixCsi(payload)), InputWheel.LineStep);
        AssertWheel(new InputDecoder().Push(WindowsVt(payload)), InputWheel.LineStep);
    }

    [Fact]
    public void Push_X10WheelDown()
    {
        var payload = "\u001b[M" + (char)(65 + 32) + "!!";
        AssertWheel(new InputDecoder().Push(WindowsVt(payload)), -InputWheel.LineStep);
        AssertWheel(new InputDecoder().Push(UnixCsi(payload)), -InputWheel.LineStep);
    }

    [Fact]
    public void Push_SgrLeftClick_IsDropped()
    {
        var payload = "\u001b[<0;12;8M";
        Assert.Empty(new InputDecoder().Push(WindowsVt(payload)));
        Assert.Empty(new InputDecoder().Push(UnixCsi(payload)));
    }

    [Fact]
    public void Push_BatchedCsiUp_IsWheelOnEveryPlatform()
    {
        var payload = "\u001b[A\u001b[A\u001b[A";
        AssertWheel(new InputDecoder().Push(WindowsVt(payload)), InputWheel.LineStep * 3);
        AssertWheel(new InputDecoder().Push(UnixCsi(payload)), InputWheel.LineStep * 3);

        var linux = Linux(
            new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        AssertWheel(new InputDecoder().Push(linux), InputWheel.LineStep * 2);
    }

    [Fact]
    public void Push_SingleCsiUp_IsArrowKey()
    {
        var events = new InputDecoder().Push(WindowsVt("\u001b[A"));
        var key = Assert.IsType<InputKey>(Assert.Single(events));
        Assert.Equal(ConsoleKey.UpArrow, key.Key);
        Assert.False(ScrollInput.TryKeyScroll(key, composerEmpty: false, pickerOpen: false, 8, out _));
        Assert.True(ScrollInput.TryKeyScroll(key, composerEmpty: true, pickerOpen: false, 8, out var delta));
        Assert.Equal(InputWheel.LineStep, delta);
    }

    [Fact]
    public void Push_KittyAndCsiTabEnterBackspace()
    {
        AssertKey(new InputDecoder().Push(WindowsVt("\u001b[9u")), ConsoleKey.Tab, '\t');
        AssertKey(new InputDecoder().Push(WindowsVt("\u001b[Z")), ConsoleKey.Tab, '\t');
        AssertKey(new InputDecoder().Push(WindowsVt("\u001b[13u")), ConsoleKey.Enter, '\r');
        AssertKey(new InputDecoder().Push(WindowsVt("\u001b[127u")), ConsoleKey.Backspace, '\b');
        AssertKey(new InputDecoder().Push(UnixCsi("\u001b[127u")), ConsoleKey.Backspace, '\b');
        var shiftTab = Assert.IsType<InputKey>(Assert.Single(new InputDecoder().Push(WindowsVt("\u001b[9;2u"))));
        Assert.Equal(ConsoleKey.Tab, shiftTab.Key);
        Assert.True(shiftTab.Modifiers.HasFlag(ConsoleModifiers.Shift));
    }

    [Fact]
    public void Push_MacOptionB_IsAltB()
    {
        var option = new List<ConsoleKeyInfo>
        {
            new('\u001b', ConsoleKey.Escape, false, false, false),
            new('b', ConsoleKey.B, false, false, false)
        };
        var windowsOption = WindowsVt("\u001bb");
        AssertAltB(new InputDecoder().Push(option));
        AssertAltB(new InputDecoder().Push(windowsOption));

        var nativeAlt = Mac(new ConsoleKeyInfo('b', ConsoleKey.B, false, true, false));
        AssertAltB(new InputDecoder().Push(nativeAlt));
    }

    [Fact]
    public void Push_MacOptionBackspace_IsAltBackspace()
    {
        var option = new List<ConsoleKeyInfo>
        {
            new('\u001b', ConsoleKey.Escape, false, false, false),
            new('\u007f', ConsoleKey.None, false, false, false)
        };
        var key = Assert.IsType<InputKey>(Assert.Single(new InputDecoder().Push(option)));
        Assert.Equal(ConsoleKey.Backspace, key.Key);
        Assert.True(key.Modifiers.HasFlag(ConsoleModifiers.Alt));
    }

    [Fact]
    public void Push_CtrlW_FromControlChar()
    {
        var events = new InputDecoder().Push(WindowsVt("\u0017"));
        var key = Assert.IsType<InputKey>(Assert.Single(events));
        Assert.Equal(ConsoleKey.W, key.Key);
        Assert.True(key.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    [Fact]
    public void Push_CtrlUpCsi_HasControlModifier()
    {
        var events = new InputDecoder().Push(WindowsVt("\u001b[1;5A"));
        var key = Assert.IsType<InputKey>(Assert.Single(events));
        Assert.Equal(ConsoleKey.UpArrow, key.Key);
        Assert.True(key.Modifiers.HasFlag(ConsoleModifiers.Control));
    }

    [Fact]
    public void Push_HeuristicPaste_DoesNotSubmitOnCrlf()
    {
        var events = new InputDecoder().Push(WindowsVt("ab\r\ncd"));
        var paste = Assert.IsType<InputPaste>(Assert.Single(events));
        Assert.Equal("ab\ncd", paste.Text);
    }

    [Fact]
    public void Push_HeuristicPaste_StripsControlsAndKeepsText()
    {
        var burst = Linux(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false),
            new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
            new ConsoleKeyInfo('\u0001', ConsoleKey.A, false, false, true));
        AssertPaste(new InputDecoder().Push(burst), "a\nb");
    }

    [Fact]
    public void Push_HoldsPasteUntilEndMarker()
    {
        var windows = new InputDecoder();
        Assert.Empty(windows.Push(WindowsVt("\u001b[200~hel")));
        Assert.True(windows.IsPasteOpen);
        AssertPaste(windows.Push(WindowsVt("lo\u001b[201~")), "hello");
        Assert.False(windows.IsPasteOpen);

        var unix = new InputDecoder();
        Assert.Empty(unix.Push(UnixCsi("\u001b[200~hel")));
        AssertPaste(unix.Push(UnixCsi("lo\u001b[201~")), "hello");
    }

    [Fact]
    public void Push_ResetClearsOpenPaste()
    {
        var decoder = new InputDecoder();
        _ = decoder.Push(WindowsVt("\u001b[200~held"));
        decoder.Reset();
        Assert.False(decoder.IsPasteOpen);
        AssertKey(decoder.Push(WindowsVt("y")), ConsoleKey.Y, 'y');
    }

    [Fact]
    public void Push_IncompleteCsi_HeldAcrossBursts()
    {
        var decoder = new InputDecoder();
        Assert.Empty(decoder.Push(WindowsVt("\u001b[")));
        AssertKey(decoder.Push(WindowsVt("A")), ConsoleKey.UpArrow, '\0');
    }

    private static void AssertAltB(IReadOnlyList<IInputEvent> events)
    {
        var key = Assert.IsType<InputKey>(Assert.Single(events));
        Assert.Equal(ConsoleKey.B, key.Key);
        Assert.True(key.Modifiers.HasFlag(ConsoleModifiers.Alt));
    }

    private static void AssertTab(IReadOnlyList<IInputEvent> events)
    {
        var key = Assert.IsType<InputKey>(Assert.Single(events));
        Assert.Equal(ConsoleKey.Tab, key.Key);
    }

    private static void AssertEnter(IReadOnlyList<IInputEvent> events)
    {
        var key = Assert.IsType<InputKey>(Assert.Single(events));
        Assert.Equal(ConsoleKey.Enter, key.Key);
    }

    private static void AssertKey(IReadOnlyList<IInputEvent> events, ConsoleKey expected, char keyChar)
    {
        var key = Assert.IsType<InputKey>(Assert.Single(events));
        Assert.Equal(expected, key.Key);
        Assert.Equal(keyChar, key.KeyChar);
    }

    private static void AssertPaste(IReadOnlyList<IInputEvent> events, string text)
    {
        var paste = Assert.IsType<InputPaste>(Assert.Single(events));
        Assert.Equal(text, paste.Text);
    }

    private static void AssertWheel(IReadOnlyList<IInputEvent> events, int delta)
    {
        var wheel = Assert.IsType<InputWheel>(Assert.Single(events));
        Assert.Equal(delta, wheel.Delta);
    }

    private static List<ConsoleKeyInfo> Linux(params ConsoleKeyInfo[] keys) => [.. keys];

    private static List<ConsoleKeyInfo> Mac(params ConsoleKeyInfo[] keys) => [.. keys];

    private static List<ConsoleKeyInfo> WindowsVt(string text)
    {
        var burst = new List<ConsoleKeyInfo>();
        foreach (var ch in text)
        {
            burst.Add(new ConsoleKeyInfo(ch, default, false, false, false));
        }

        return burst;
    }

    private static List<ConsoleKeyInfo> UnixCsi(string text)
    {
        var burst = new List<ConsoleKeyInfo>();
        foreach (var ch in text)
        {
            var key = ch == '\u001b' ? ConsoleKey.Escape : ConsoleKey.None;
            burst.Add(new ConsoleKeyInfo(ch, key, false, false, false));
        }

        return burst;
    }
}
