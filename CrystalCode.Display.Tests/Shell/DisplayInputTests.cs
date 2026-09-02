using CrystalCode.Display.Input;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class DisplayInputTests
{
    [Fact]
    public void TryToggleVerbose_CtrlORequiresEmptyComposer()
    {
        var key = new InputKey(ConsoleKey.O, '\0', ConsoleModifiers.Control);

        Assert.True(DisplayInput.TryToggleVerbose(key, composerEmpty: true, pickerOpen: false, out var tools));
        Assert.Equal(DisplayInput.VerboseToggle.Tools, tools);
        Assert.False(DisplayInput.TryToggleVerbose(key, composerEmpty: false, pickerOpen: false, out _));
    }

    [Fact]
    public void TryToggleVerbose_CtrlShiftOTargetsCommands()
    {
        var key = new InputKey(
            ConsoleKey.O,
            '\0',
            ConsoleModifiers.Control | ConsoleModifiers.Shift);

        Assert.True(DisplayInput.TryToggleVerbose(key, composerEmpty: true, pickerOpen: false, out var commands));
        Assert.Equal(DisplayInput.VerboseToggle.Commands, commands);
    }
}
