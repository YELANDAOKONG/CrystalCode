using CrystalCode.Display.Input;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class DisplayInputTests
{
    [Fact]
    public void TryToggleVerbose_CtrlORequiresEmptyComposer()
    {
        var key = new InputKey(ConsoleKey.O, '\u000f', ConsoleModifiers.Control);

        Assert.True(DisplayInput.TryToggleVerbose(key, composerEmpty: true, pickerOpen: false, out var tools));
        Assert.Equal(DisplayInput.VerboseToggle.Tools, tools);
        Assert.False(DisplayInput.TryToggleVerbose(key, composerEmpty: false, pickerOpen: false, out _));
    }

    [Fact]
    public void TryToggleVerbose_CtrlGTargetsCommands()
    {
        var key = new InputKey(ConsoleKey.G, '\u0007', ConsoleModifiers.Control);

        Assert.True(DisplayInput.TryToggleVerbose(key, composerEmpty: true, pickerOpen: false, out var commands));
        Assert.Equal(DisplayInput.VerboseToggle.Commands, commands);
    }

    [Fact]
    public void TryToggleVerbose_RejectsSyntheticShiftControlO()
    {
        var synthetic = new InputKey(ConsoleKey.O, '\u000f', ConsoleModifiers.Control | ConsoleModifiers.Shift);

        Assert.False(DisplayInput.TryToggleVerbose(synthetic, composerEmpty: true, pickerOpen: false, out _));
    }

    [Fact]
    public void InputDecoder_CtrlOAndCtrlG_MapToDistinctVerboseToggles()
    {
        var decoder = new InputDecoder();
        var ctrlO = Assert.IsType<InputKey>(Assert.Single(decoder.Push(WindowsVt("\u000f"))));
        var ctrlG = Assert.IsType<InputKey>(Assert.Single(decoder.Push(WindowsVt("\u0007"))));

        Assert.True(DisplayInput.TryToggleVerbose(ctrlO, composerEmpty: true, pickerOpen: false, out var tools));
        Assert.Equal(DisplayInput.VerboseToggle.Tools, tools);
        Assert.True(DisplayInput.TryToggleVerbose(ctrlG, composerEmpty: true, pickerOpen: false, out var commands));
        Assert.Equal(DisplayInput.VerboseToggle.Commands, commands);
    }

    private static IReadOnlyList<ConsoleKeyInfo> WindowsVt(string text)
    {
        var burst = new List<ConsoleKeyInfo>(text.Length);
        foreach (var ch in text)
        {
            burst.Add(new ConsoleKeyInfo(ch, ConsoleKey.None, false, false, false));
        }

        return burst;
    }
}
