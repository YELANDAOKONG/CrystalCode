using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ComposerBufferTests
{
    [Fact]
    public void Handle_ShiftTabTogglesPlan_TabDoesNot()
    {
        var buffer = new ComposerBuffer();
        var tab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
        var shiftTab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false);

        Assert.Equal(ComposerAction.None, buffer.Handle(tab));
        Assert.Equal(ComposerAction.TogglePlan, buffer.Handle(shiftTab));
    }

    [Fact]
    public void Handle_EnterSubmits_CtrlJInsertsNewline()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("hello");
        var newline = new ConsoleKeyInfo('\n', ConsoleKey.J, false, false, true);
        var enter = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);

        Assert.Equal(ComposerAction.None, buffer.Handle(newline));
        Assert.Contains('\n', buffer.Text);
        Assert.Equal(ComposerAction.Submit, buffer.Handle(enter));
    }

    [Fact]
    public void Handle_QuestionWhenEmptyShowsHelp()
    {
        var buffer = new ComposerBuffer();
        var key = new ConsoleKeyInfo('?', ConsoleKey.Oem2, false, false, false);

        Assert.Equal(ComposerAction.ShowHelp, buffer.Handle(key));
    }
}
