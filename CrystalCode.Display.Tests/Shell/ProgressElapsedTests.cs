using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ProgressElapsedTests
{
    [Theory]
    [InlineData(0, "0s")]
    [InlineData(5, "5s")]
    [InlineData(59, "59s")]
    [InlineData(60, "1m0s")]
    [InlineData(138, "2m18s")]
    [InlineData(3723, "1h2m3s")]
    public void Format_CompactUnits(int seconds, string expected)
    {
        Assert.Equal(expected, ProgressElapsed.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Format_ClampsNegativeToZero()
    {
        Assert.Equal("0s", ProgressElapsed.Format(TimeSpan.FromSeconds(-3)));
    }
}
