using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ShellLayoutTests
{
    [Fact]
    public void Measure_RowsSumToHeight()
    {
        var regions = ShellLayout.Measure(80, 24, composerWanted: 3, overlayWanted: 2, queueWanted: 4);

        Assert.Equal(
            24,
            regions.TranscriptRows
                + regions.OverlayRows
                + regions.ProgressRows
                + ShellLayout.StatusRows
                + regions.QueueRows
                + regions.ComposerRows);
        Assert.Equal(2, regions.OverlayRows);
        Assert.Equal(0, regions.ProgressRows);
        Assert.Equal(4, regions.QueueRows);
        Assert.Equal(3, regions.ComposerRows);
        Assert.Equal(regions.TranscriptRows, regions.OverlayTop);
        Assert.Equal(regions.OverlayTop + regions.OverlayRows, regions.ProgressTop);
        Assert.Equal(regions.ProgressTop + regions.ProgressRows, regions.StatusTop);
        Assert.Equal(regions.StatusTop + ShellLayout.StatusRows, regions.QueueTop);
        Assert.Equal(regions.QueueTop + regions.QueueRows, regions.ComposerTop);
    }

    [Fact]
    public void Measure_ProgressSitsAboveStatus()
    {
        var regions = ShellLayout.Measure(
            80,
            24,
            composerWanted: 2,
            overlayWanted: 0,
            queueWanted: 0,
            progressWanted: 1);

        Assert.Equal(1, regions.ProgressRows);
        Assert.Equal(regions.OverlayTop + regions.OverlayRows, regions.ProgressTop);
        Assert.Equal(regions.ProgressTop + 1, regions.StatusTop);
        Assert.Equal(
            24,
            regions.TranscriptRows
                + regions.OverlayRows
                + regions.ProgressRows
                + ShellLayout.StatusRows
                + regions.QueueRows
                + regions.ComposerRows);
    }
}
