using CrystalCode.Display.Composer;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Composer;

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
    public void Handle_CtrlVRequestsImagePasteWithoutChangingText()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("inspect ");
        var key = new ConsoleKeyInfo('\x16', ConsoleKey.V, false, false, true);

        var action = buffer.Handle(key);

        Assert.Equal(ComposerAction.PasteImage, action);
        Assert.Equal("inspect ", buffer.Text);
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
    public void Handle_BackspaceRemovesAtomicImageMarker()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("inspect ");
        buffer.InsertAtomic("[Image #1]");

        buffer.Handle(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));

        Assert.Equal("inspect ", buffer.Text);
        Assert.Equal(buffer.Text.Length, buffer.Cursor);
    }

    [Fact]
    public void Project_ColorsOnlyAttachedMarkerWithTheSameTypedText()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("[Image #1] ");
        buffer.InsertAtomic("[Image #1]");

        var view = buffer.Project(80, 8);

        Assert.Equal("Work > [Image #1] [Image #1]", Assert.Single(view.Lines).Plain);
        Assert.Contains($"[{Theme.Image}]", view.Lines[0].Markup, StringComparison.Ordinal);
        Assert.Equal(1, view.Lines[0].Markup.Split($"[{Theme.Image}]").Length - 1);
        Assert.Equal("[Image #1] " + ComposerBuffer.ImageMarkerPrefix + "[Image #1]",
            buffer.SubmissionText);

        buffer.Handle(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        Assert.Equal("[Image #1] ", buffer.Text);
        Assert.Equal(buffer.Text, buffer.SubmissionText);

        buffer.Handle(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        buffer.Handle(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
        Assert.Equal("[Image #1", buffer.Text);
    }

    [Fact]
    public void Handle_HistoryNavigationRestoresAttachedMarkerIdentity()
    {
        var buffer = new ComposerBuffer();
        buffer.SeedHistory(["older"]);
        buffer.InsertAtomic("[Image #1]");

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        Assert.Equal(ComposerBuffer.ImageMarkerPrefix + "[Image #1]",
            buffer.SubmissionText);
    }

    [Fact]
    public void Project_ColorsAttachedMarkerOnLaterComposerRow()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("request\n");
        buffer.InsertAtomic("[Image #1]");

        var view = buffer.Project(30, 8);

        Assert.Equal(2, view.Lines.Count);
        Assert.Contains($"[{Theme.Image}]", view.Lines[1].Markup, StringComparison.Ordinal);
        Assert.EndsWith("[Image #1]", view.Lines[1].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Handle_ArrowAndDeleteTreatImageMarkerAsOneUnit()
    {
        var buffer = new ComposerBuffer();
        buffer.InsertAtomic("[Image #2]");
        buffer.Insert(" later");
        for (var index = 0; index < " later".Length; index++)
        {
            buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        }

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        Assert.Equal(0, buffer.Cursor);
        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        Assert.Equal("[Image #2]".Length, buffer.Cursor);
        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));

        Assert.Equal(" later", buffer.Text);
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
    public void Handle_EmptyUpRecallsHistoryAndDownRestoresDraft()
    {
        var buffer = new ComposerBuffer();
        buffer.SeedHistory(["older", "newer"]);
        buffer.Insert("draft");
        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal("newer", buffer.Text);
        Assert.Equal(0, buffer.Cursor);

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.Equal("draft", buffer.Text);
        Assert.Equal(4, buffer.Cursor);
    }

    [Fact]
    public void Handle_EmptyPromptUpRecallsLatestSubmission()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("previous prompt");
        buffer.RememberAndClear();

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.Equal("previous prompt", buffer.Text);
    }

    [Fact]
    public void Handle_ArrowsMoveWithinMultilineBeforeHistory()
    {
        var buffer = new ComposerBuffer();
        buffer.SeedHistory(["history"]);
        buffer.Insert("one\ntwo\nthree");
        buffer.Project(40, 8);

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal("one\ntwo\nthree", buffer.Text);
        Assert.Equal("one\ntwo".Length, buffer.Cursor);

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal(3, buffer.Cursor);

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal("history", buffer.Text);
    }

    [Fact]
    public void Handle_ArrowsMoveAcrossWrappedRows()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("abcdefghijklmnop");
        buffer.Project(16, 8);

        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.Equal("abcdefghijklmnop", buffer.Text);
        Assert.True(buffer.Cursor < buffer.Text.Length);
    }

    [Fact]
    public void ForgetImageHistory_DropsEntriesWithImageMarkers()
    {
        var buffer = new ComposerBuffer();
        buffer.InsertAtomic("[Image #1]");
        buffer.RememberAndClear();
        buffer.Insert("text");
        buffer.RememberAndClear();

        buffer.ForgetImageHistory();
        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal("text", buffer.Text);
        buffer.Handle(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal("text", buffer.Text);
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

    [Fact]
    public void Project_UsesTheVisibleComposerRowsWhenOtherRegionsConsumeSpace()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("one\ntwo\nthree\nfour\nfive\nsix\nseven\neight");
        var initial = buffer.Project(80, ShellLayout.MaxComposerRows);
        var regions = ShellLayout.Measure(
            80, 24, initial.Lines.Count, overlayWanted: 8, queueWanted: 8, progressWanted: 1);
        var visible = buffer.Project(80, regions.ComposerRows);
        var frame = FrameRows.Assemble(regions, [], [], PaintLine.Blank, [], visible);

        Assert.Equal(3, regions.ComposerRows);
        Assert.Equal(["       six", "       seven", "       eight"],
            visible.Lines.Select(line => line.Plain));
        Assert.Equal(2, visible.CursorRow);
        Assert.Equal("       eight", frame[regions.ComposerTop + visible.CursorRow].Plain);
    }

    [Fact]
    public void Project_ExpandsPastedTabsWithoutChangingSubmission()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("a\tb");

        var view = buffer.Project(80, 8);

        Assert.Equal("a\tb", buffer.SubmissionText);
        Assert.Equal("Work > a    b", Assert.Single(view.Lines).Plain);
        Assert.DoesNotContain('\t', view.Lines[0].Markup);
        Assert.Equal(TextWidth.Measure(view.Lines[0].Plain), view.CursorColumn);
    }

    [Fact]
    public void Project_ReservesTheLastColumnForTheEndCursor()
    {
        var buffer = new ComposerBuffer();
        buffer.Insert("abcdefgh");

        var view = buffer.Project(16, 8);

        Assert.Equal(15, TextWidth.Measure(Assert.Single(view.Lines).Plain));
        Assert.Equal(15, view.CursorColumn);
    }
}
