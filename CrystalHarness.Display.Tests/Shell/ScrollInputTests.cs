using CrystalHarness.Display.Input;
using CrystalHarness.Display.Shell;

using Xunit;

namespace CrystalHarness.Display.Tests.Shell;

public sealed class ScrollInputTests
{
    [Fact]
    public void TryKeyScroll_PageUpMovesTowardOlderRows()
    {
        var pageUp = new InputKey(ConsoleKey.PageUp, '\0', ConsoleModifiers.None);

        Assert.True(ScrollInput.TryKeyScroll(pageUp, false, false, 10, out var delta));
        Assert.Equal(10, delta);
    }

    [Fact]
    public void TryKeyScroll_CtrlUpScrolls_EmptyUpScrolls_TypedUpDoesNot()
    {
        var plainUp = new InputKey(ConsoleKey.UpArrow, '\0', ConsoleModifiers.None);
        var ctrlUp = new InputKey(ConsoleKey.UpArrow, '\0', ConsoleModifiers.Control);

        Assert.True(ScrollInput.TryKeyScroll(plainUp, composerEmpty: true, pickerOpen: false, 10, out var emptyDelta));
        Assert.Equal(ScrollInput.LineStep, emptyDelta);
        Assert.False(ScrollInput.TryKeyScroll(plainUp, composerEmpty: false, pickerOpen: false, 10, out _));
        Assert.True(ScrollInput.TryKeyScroll(ctrlUp, composerEmpty: false, pickerOpen: false, 10, out var delta));
        Assert.Equal(ScrollInput.LineStep, delta);
    }
}
