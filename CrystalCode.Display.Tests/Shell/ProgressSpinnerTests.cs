using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ProgressSpinnerTests
{
    [Fact]
    public void Frame_WrapsAround()
    {
        Assert.Equal(ProgressSpinner.Frame(0), ProgressSpinner.Frame(ProgressSpinner.FrameCount));
        Assert.NotEqual(ProgressSpinner.Frame(0), ProgressSpinner.Frame(1));
    }

    [Fact]
    public void Frame_EachGlyphIsOneCell()
    {
        for (var i = 0; i < ProgressSpinner.FrameCount; i++)
        {
            var glyph = ProgressSpinner.Frame(i);
            Assert.Equal(1, TextWidth.Measure(glyph));
        }
    }
}
