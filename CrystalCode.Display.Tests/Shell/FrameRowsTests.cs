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
