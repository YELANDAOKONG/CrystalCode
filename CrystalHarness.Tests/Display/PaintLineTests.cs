using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class PaintLineTests
{
    [Fact]
    public void Fit_LeavesShortLineUnchanged()
    {
        var line = PaintLine.Colored(Theme.Chrome, "hello");

        var fitted = line.Fit(20);

        Assert.Equal(line, fitted);
    }

    [Fact]
    public void Fit_TruncatesWidePlain()
    {
        var line = new PaintLine("[grey50]hello world[/]", "hello world");

        var fitted = line.Fit(8);

        Assert.True(TextWidth.Measure(fitted.Plain) <= 8);
        Assert.StartsWith("hello", fitted.Plain, StringComparison.Ordinal);
    }
}
