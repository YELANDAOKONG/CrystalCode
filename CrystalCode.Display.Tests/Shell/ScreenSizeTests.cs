using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ScreenSizeTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 24)]
    [InlineData(80, 0)]
    public void TryAccept_NonPositiveSizeUsesFallback(int width, int height)
    {
        Assert.False(ScreenSize.TryAccept(width, height, out var acceptedWidth, out var acceptedHeight));
        Assert.Equal(ScreenSize.FallbackWidth, acceptedWidth);
        Assert.Equal(ScreenSize.FallbackHeight, acceptedHeight);
    }

    [Fact]
    public void TryAccept_PositiveSizeStaysRaw()
    {
        Assert.True(ScreenSize.TryAccept(20, 10, out var width, out var height));
        Assert.Equal(20, width);
        Assert.Equal(10, height);
    }
}
