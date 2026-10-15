using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class ContextWindowSelectionTests
{
    [Theory]
    [InlineData("272k", 272000)]
    [InlineData("1M", 1000000)]
    [InlineData("1.5m", 1500000)]
    [InlineData("272000", 272000)]
    public void TryParse_AcceptsTokenCountsAndDecimalSuffixes(string text, int expected)
    {
        Assert.True(ContextWindowSelection.TryParse(text, out var window));
        Assert.Equal(expected, window);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999999999999m")]
    [InlineData("0.0001k")]
    [InlineData("NaN")]
    [InlineData("272k extra")]
    public void TryParse_RejectsInvalidOrOverflowingCounts(string text)
    {
        Assert.False(ContextWindowSelection.TryParse(text, out _));
    }

    [Fact]
    public void Constructor_CopiesAndSortsWindowsWithoutChangingMaximum()
    {
        int[] windows = [1000000, 272000];
        var model = new ModelSettings(1000000, contextWindows: windows);
        windows[1] = 1;

        Assert.Equal(1000000, model.ContextWindow);
        Assert.Equal([272000, 1000000], model.ContextWindows);
        Assert.Equal([8192], new ModelSettings(8192).ContextWindows);
    }

    [Theory]
    [InlineData(0, 1000000)]
    [InlineData(-1, 1000000)]
    [InlineData(272000, 272000)]
    [InlineData(272000, 1000001)]
    public void Constructor_RejectsInvalidWindows(int first, int second)
    {
        Assert.Throws<ArgumentException>(() =>
            new ModelSettings(1000000, contextWindows: [first, second]));
    }
}
