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
                + ShellLayout.StatusRows
                + regions.QueueRows
                + regions.ComposerRows);
        Assert.Equal(2, regions.OverlayRows);
        Assert.Equal(4, regions.QueueRows);
        Assert.Equal(3, regions.ComposerRows);
        Assert.Equal(regions.TranscriptRows, regions.OverlayTop);
        Assert.Equal(regions.StatusTop + ShellLayout.StatusRows, regions.QueueTop);
        Assert.Equal(regions.QueueTop + regions.QueueRows, regions.ComposerTop);
    }
}
