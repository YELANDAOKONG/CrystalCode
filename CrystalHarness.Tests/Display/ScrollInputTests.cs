using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ScrollInputTests
{
    [Fact]
    public void TryDelta_PageUpMovesTowardOlderRows()
    {
        var pageUp = new ConsoleKeyInfo('\0', ConsoleKey.PageUp, false, false, false);

        Assert.True(ScrollInput.TryDelta([pageUp], false, false, 10, out var delta));
        Assert.Equal(10, delta);
    }

    [Fact]
    public void TryDelta_CtrlUpScrolls_PlainUpDoesNotScroll()
    {
        var plainUp = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);
        var ctrlUp = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, true);

        Assert.False(ScrollInput.TryDelta([plainUp], composerEmpty: true, pickerOpen: false, 10, out _));
        Assert.True(ScrollInput.TryDelta([ctrlUp], composerEmpty: true, pickerOpen: false, 10, out var delta));
        Assert.Equal(ScrollInput.LineStep, delta);
    }

    [Fact]
    public void TryDelta_CsiPageUpIsNotPaste()
    {
        var burst = Csi("\u001b[5~");

        Assert.True(ScrollInput.TryDelta(burst, false, false, 8, out var delta));
        Assert.Equal(8, delta);
        Assert.False(ScrollInput.IsPaste(burst));
    }

    [Fact]
    public void TryDelta_SgrWheelUpScrolls()
    {
        var burst = Csi("\u001b[<64;12;8M");

        Assert.True(ScrollInput.TryDelta(burst, false, false, 8, out var delta));
        Assert.Equal(ScrollInput.LineStep, delta);
        Assert.False(ScrollInput.IsPaste(burst));
    }

    [Fact]
    public void IsPaste_PrintableBurst()
    {
        var burst = new List<ConsoleKeyInfo>
        {
            new('a', ConsoleKey.A, false, false, false),
            new('b', ConsoleKey.B, false, false, false)
        };

        Assert.True(ScrollInput.IsPaste(burst));
        Assert.False(ScrollInput.TryDelta(burst, true, false, 8, out _));
    }

    [Fact]
    public void TryComposerKey_DecodesCsiArrow()
    {
        Assert.True(ScrollInput.TryComposerKey(Csi("\u001b[A"), out var key));
        Assert.Equal(ConsoleKey.UpArrow, key.Key);
    }

    private static List<ConsoleKeyInfo> Csi(string text)
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
