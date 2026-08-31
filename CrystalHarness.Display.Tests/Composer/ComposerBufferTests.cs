using CrystalHarness.Display.Composer;
using CrystalHarness.Display.Paint;

using Xunit;

namespace CrystalHarness.Display.Tests.Composer;

public sealed class ComposerBufferTests
{
    [Fact]
    public void Handle_TabTogglesPlan()
    {
        var buffer = new ComposerBuffer();
        var tab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
        var shiftTab = new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false);
        var vtTab = new ConsoleKeyInfo('\t', default, false, false, false);

        Assert.Equal(ComposerAction.TogglePlan, buffer.Handle(tab));
        Assert.Equal(ComposerAction.TogglePlan, buffer.Handle(shiftTab));
        Assert.Equal(ComposerAction.TogglePlan, buffer.Handle(vtTab));
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

        buffer.Replace("queued");
        var vtEnter = new ConsoleKeyInfo('\r', default, false, false, false);
        Assert.Equal(ComposerAction.Submit, buffer.Handle(vtEnter));
    }

    [Fact]
    public void Handle_QuestionWhenEmptyShowsHelp()
    {
        var buffer = new ComposerBuffer();
        var key = new ConsoleKeyInfo('?', ConsoleKey.Oem2, false, false, false);

        Assert.Equal(ComposerAction.ShowHelp, buffer.Handle(key));
    }

    [Fact]
    public void Handle_WordNavigationAndDeletion()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("hello world test");

        // Ctrl+W deletes word left
        var ctrlW = new ConsoleKeyInfo('\x17', ConsoleKey.W, false, false, true);
        buffer.Handle(ctrlW);
        Assert.Equal("hello world ", buffer.Text);

        // Alt+Backspace deletes word left
        var altBackspace = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, true, false);
        buffer.Handle(altBackspace);
        Assert.Equal("hello ", buffer.Text);

        // Ctrl+U clears line before cursor
        var ctrlU = new ConsoleKeyInfo('\x15', ConsoleKey.U, false, false, true);
        buffer.Handle(ctrlU);
        Assert.Equal(string.Empty, buffer.Text);
    }

    [Fact]
    public void Handle_ControlBackspaceFollowsHostPlatform()
    {
        var buffer = new ComposerBuffer();
        var chord = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, true);
        if (OperatingSystem.IsWindows())
        {
            buffer.Insert("hello world");
            buffer.Handle(chord);
            Assert.Equal("hello ", buffer.Text);
            return;
        }

        buffer.Insert("查看其他问题");
        buffer.Handle(chord);
        Assert.Equal("查看其他问", buffer.Text);
    }

    [Fact]
    public void Handle_PlainBackspaceDeletesOneCharacter()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("hello");
        var backspace = new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false);

        buffer.Handle(backspace);

        Assert.Equal("hell", buffer.Text);
    }

    [Fact]
    public void Handle_HistoryRecall_CtrlP_CtrlN()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("first prompt");
        buffer.RememberAndClear();

        // Now empty buffer, Ctrl+P recalls previous
        var ctrlP = new ConsoleKeyInfo('\x10', ConsoleKey.P, false, false, true);
        buffer.Handle(ctrlP);
        Assert.Equal("first prompt", buffer.Text);

        // Ctrl+N goes back forward to empty
        var ctrlN = new ConsoleKeyInfo('\x0E', ConsoleKey.N, false, false, true);
        buffer.Handle(ctrlN);
        Assert.Equal(string.Empty, buffer.Text);
    }

    [Fact]
    public void Project_EmptyKeepsPromptAndPlaceholderOnOneLine()
    {
        var buffer = new ComposerBuffer();
        var view = buffer.Project(40, 8);

        Assert.Single(view.Lines);
        Assert.StartsWith("Work > ", view.Lines[0].Plain, StringComparison.Ordinal);
        Assert.Contains("Ask anything", view.Lines[0].Plain, StringComparison.Ordinal);
        Assert.True(TextWidth.Measure(view.Lines[0].Plain) <= 40);
        Assert.Equal(0, view.CursorRow);
        Assert.Equal(TextWidth.Measure("Work > "), view.CursorColumn);
    }
}
