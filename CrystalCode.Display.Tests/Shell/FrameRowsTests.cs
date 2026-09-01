using Xunit;

using CrystalCode.Display.Composer;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

namespace CrystalCode.Display.Tests.Shell;

public sealed class FrameRowsTests
{
    [Fact]
    public void Assemble_FillsHeightAndFitsWidth()
    {
        var regions = ShellLayout.Measure(20, 10, composerWanted: 2, overlayWanted: 0, queueWanted: 0);
        var transcript = new[] { PaintLine.Colored("grey84", new string('x', 40)) };
        var composer = new ComposerView([PaintLine.Colored("grey50", "plan")], 0, 1);

        var frame = FrameRows.Assemble(
            regions,
            transcript,
            [],
            PaintLine.Colored("grey50", "status"),
            [],
            composer);

        Assert.Equal(regions.Height, frame.Count);
        Assert.True(TextWidth.Measure(frame[0].Plain) <= regions.Width);
        Assert.Contains(frame, line => line.Plain.Contains("status", StringComparison.Ordinal));
        Assert.Contains(frame, line => line.Plain.Contains("plan", StringComparison.Ordinal));
    }

    [Fact]
    public void Assemble_PlacesProgressImmediatelyAboveStatus()
    {
        var regions = ShellLayout.Measure(
            20,
            10,
            composerWanted: 1,
            overlayWanted: 0,
            queueWanted: 0,
            progressWanted: 1);
        var composer = new ComposerView([PaintLine.Colored("grey50", "work")], 0, 1);

        var frame = FrameRows.Assemble(
            regions,
            [PaintLine.Colored("grey84", "log")],
            [],
            PaintLine.Colored("grey50", "status"),
            [],
            composer,
            PaintLine.Colored("lightsteelblue", "  Awaiting Approval"));

        Assert.Equal("  Awaiting Approval", frame[regions.ProgressTop].Plain);
        Assert.Equal("status", frame[regions.StatusTop].Plain);
        Assert.Equal(regions.ProgressTop + 1, regions.StatusTop);
    }

    [Fact]
    public void Notice_CentersMessageInFullHeightFrame()
    {
        var frame = FrameRows.Notice(40, 10, "too small");

        Assert.Equal(10, frame.Count);
        Assert.Equal("too small", frame[4].Plain.Trim());
        Assert.StartsWith(new string(' ', 15), frame[4].Plain, StringComparison.Ordinal);
        Assert.All(frame.Where((_, index) => index != 4), line => Assert.Equal(PaintLine.Blank, line));
    }

    [Fact]
    public void Notice_TruncatesMessageToWidth()
    {
        var frame = FrameRows.Notice(8, 3, "too small notice");

        Assert.Equal(3, frame.Count);
        Assert.True(TextWidth.Measure(frame[1].Plain) <= 8);
        Assert.Contains("too", frame[1].Plain, StringComparison.Ordinal);
        Assert.Contains(Theme.Fail, frame[1].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Theme.Chrome, frame[1].Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_SurvivesTinyAndZeroHeights()
    {
        var one = FrameRows.Notice(10, 1, "too small");
        var floor = FrameRows.Notice(10, 0, "too small");

        Assert.Single(one);
        Assert.Single(floor);
        Assert.Equal("too small", one[0].Plain.Trim());
        Assert.Equal("too small", floor[0].Plain.Trim());
    }

    [Fact]
    public void Notice_SurvivesZeroWidth()
    {
        var frame = FrameRows.Notice(0, 4, "too small");

        Assert.Equal(4, frame.Count);
        Assert.All(frame, line => Assert.Equal(PaintLine.Blank, line));
    }

    [Fact]
    public void Dirty_AllRowsWhenPreviousMissingOrSizeChanges()
    {
        var current = new[] { PaintLine.Colored("grey50", "a"), PaintLine.Blank };
        Assert.Equal([0, 1], FrameRows.Dirty(null, current));
        Assert.Equal([0, 1], FrameRows.Dirty([PaintLine.Blank], current));
    }

    [Fact]
    public void Dirty_OnlyChangedRows()
    {
        var previous = new[]
        {
            PaintLine.Colored("grey50", "keep"),
            PaintLine.Colored("grey50", "old"),
            PaintLine.Blank
        };
        var current = new[]
        {
            PaintLine.Colored("grey50", "keep"),
            PaintLine.Colored("grey50", "new"),
            PaintLine.Blank
        };

        Assert.Equal([1], FrameRows.Dirty(previous, current));
    }
}
