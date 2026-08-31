using CrystalHarness.Display.Composer;
using CrystalHarness.Display.Shell;

using Xunit;

namespace CrystalHarness.Display.Tests.Shell;

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
    public void TryDelta_CtrlUpScrolls_EmptyUpScrolls_TypedUpDoesNot()
    {
        var plainUp = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);
        var ctrlUp = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, true);

        Assert.True(ScrollInput.TryDelta([plainUp], composerEmpty: true, pickerOpen: false, 10, out var emptyDelta));
        Assert.Equal(ScrollInput.LineStep, emptyDelta);
        Assert.False(ScrollInput.TryDelta([plainUp], composerEmpty: false, pickerOpen: false, 10, out _));
        Assert.True(ScrollInput.TryDelta([ctrlUp], composerEmpty: false, pickerOpen: false, 10, out var delta));
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
    public void TryDelta_SgrWheelWithNullEscapeCharStillScrolls()
    {
        var burst = new List<ConsoleKeyInfo>
        {
            new('\0', ConsoleKey.Escape, false, false, false)
        };
        foreach (var ch in "[<64;12;8M")
        {
            burst.Add(new ConsoleKeyInfo(ch, ConsoleKey.None, false, false, false));
        }

        Assert.True(ScrollInput.TryDelta(burst, composerEmpty: false, pickerOpen: false, 8, out var delta));
        Assert.Equal(ScrollInput.LineStep, delta);
    }

    [Fact]
    public void TryDelta_SgrWheelBurstSumsClicks()
    {
        var burst = Csi("\u001b[<64;1;1M\u001b[<65;1;1M\u001b[<64;1;1M");

        Assert.True(ScrollInput.TryDelta(burst, false, false, 8, out var delta));
        Assert.Equal(ScrollInput.LineStep, delta);
    }

    [Fact]
    public void TryDelta_X10WheelDownScrolls()
    {
        var burst = Csi("\u001b[M" + (char)(65 + 32) + "!!");

        Assert.True(ScrollInput.TryDelta(burst, false, false, 8, out var delta));
        Assert.Equal(-ScrollInput.LineStep, delta);
    }

    [Fact]
    public void TryDelta_SgrLeftClickIsNotScrollOrPaste()
    {
        var burst = Csi("\u001b[<0;12;8M");

        Assert.False(ScrollInput.TryDelta(burst, false, false, 8, out _));
        Assert.False(ScrollInput.IsPaste(burst));
        Assert.False(ScrollInput.TryComposerKeys(burst, out _));
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
    public void IsPaste_BracketedPasteMarkersAreNotHeuristicPaste()
    {
        var burst = Csi(BracketedPaste.StartMarker + "hi" + BracketedPaste.EndMarker);

        Assert.False(ScrollInput.IsPaste(burst));
    }

    [Fact]
    public void TryComposerKey_DecodesCsiArrow()
    {
        Assert.True(ScrollInput.TryComposerKey(Csi("\u001b[A"), out var key));
        Assert.Equal(ConsoleKey.UpArrow, key.Key);
    }

    [Fact]
    public void TryComposerKeys_RepeatsBackspace()
    {
        var burst = new List<ConsoleKeyInfo>
        {
            new('\b', ConsoleKey.Backspace, false, false, true),
            new('\b', ConsoleKey.Backspace, false, false, true)
        };

        Assert.True(ScrollInput.TryComposerKeys(burst, out var keys));
        Assert.Equal(2, keys.Count);
        Assert.False(ScrollInput.IsPaste(burst));
    }

    [Fact]
    public void TryComposerKey_MapsKittyBackspace()
    {
        Assert.True(ScrollInput.TryComposerKey(Csi("\u001b[127u"), out var key));
        Assert.Equal(ConsoleKey.Backspace, key.Key);
    }

    [Fact]
    public void TryComposerKey_RecoversTabAndEnterWhenKeyIsEmpty()
    {
        var tab = new ConsoleKeyInfo('\t', default, false, false, false);
        var enter = new ConsoleKeyInfo('\r', default, false, false, false);
        var letter = new ConsoleKeyInfo('y', default, false, false, false);

        Assert.True(ScrollInput.TryComposerKey([tab], out var mappedTab));
        Assert.Equal(ConsoleKey.Tab, mappedTab.Key);
        Assert.True(ScrollInput.TryComposerKey([enter], out var mappedEnter));
        Assert.Equal(ConsoleKey.Enter, mappedEnter.Key);
        Assert.True(ScrollInput.TryComposerKey([letter], out var mappedLetter));
        Assert.Equal(ConsoleKey.Y, mappedLetter.Key);
    }

    [Fact]
    public void TryComposerKey_MapsCrLfBurstAsEnter()
    {
        var burst = new List<ConsoleKeyInfo>
        {
            new('\r', default, false, false, false),
            new('\n', default, false, false, false)
        };

        Assert.True(ScrollInput.TryComposerKey(burst, out var key));
        Assert.Equal(ConsoleKey.Enter, key.Key);
        Assert.False(ScrollInput.IsPaste(burst));
    }

    [Fact]
    public void TryComposerKey_MapsCsiTabAndShiftTab()
    {
        Assert.True(ScrollInput.TryComposerKey(Csi("\u001b[9u"), out var tab));
        Assert.Equal(ConsoleKey.Tab, tab.Key);
        Assert.False(tab.Modifiers.HasFlag(ConsoleModifiers.Shift));
        Assert.True(ScrollInput.TryComposerKey(Csi("\u001b[Z"), out var shiftTab));
        Assert.Equal(ConsoleKey.Tab, shiftTab.Key);
        Assert.True(shiftTab.Modifiers.HasFlag(ConsoleModifiers.Shift));
        Assert.True(ScrollInput.TryComposerKey(Csi("\u001b[13u"), out var enter));
        Assert.Equal(ConsoleKey.Enter, enter.Key);
    }

    [Fact]
    public void TryDelta_CsiUpScrollsWhenComposerEmpty()
    {
        var burst = Csi("\u001b[A");

        Assert.True(ScrollInput.TryDelta(burst, composerEmpty: true, pickerOpen: false, 8, out var delta));
        Assert.Equal(ScrollInput.LineStep, delta);
        Assert.False(ScrollInput.TryDelta(burst, composerEmpty: false, pickerOpen: false, 8, out _));
    }

    [Fact]
    public void TryDelta_BatchedCsiUpScrollsEvenWhenComposerHasText()
    {
        var burst = Csi("\u001b[A\u001b[A\u001b[A");

        Assert.True(ScrollInput.TryDelta(burst, composerEmpty: false, pickerOpen: false, 8, out var delta));
        Assert.Equal(ScrollInput.LineStep * 3, delta);
        Assert.False(ScrollInput.TryComposerKeys(burst, out _));
        Assert.False(ScrollInput.IsPaste(burst));
    }

    [Fact]
    public void TryDelta_RepeatedArrowKeysScroll()
    {
        var burst = new List<ConsoleKeyInfo>
        {
            new('\0', ConsoleKey.UpArrow, false, false, false),
            new('\0', ConsoleKey.UpArrow, false, false, false)
        };

        Assert.True(ScrollInput.TryDelta(burst, composerEmpty: false, pickerOpen: false, 8, out var delta));
        Assert.Equal(ScrollInput.LineStep * 2, delta);
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
