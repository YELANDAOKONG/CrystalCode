using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ShellLayoutTests
{
    [Fact]
    public void Measure_RowsSumToHeight()
    {
        var regions = ShellLayout.Measure(80, 24, composerWanted: 3, overlayWanted: 2);

        Assert.Equal(24, regions.TranscriptRows + regions.OverlayRows + ShellLayout.StatusRows + regions.ComposerRows);
        Assert.Equal(2, regions.OverlayRows);
        Assert.Equal(3, regions.ComposerRows);
        Assert.Equal(regions.TranscriptRows, regions.OverlayTop);
    }
}
