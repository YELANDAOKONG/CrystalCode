using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class TranscriptScrollbarTests
{
    [Fact]
    public void ThumbOf_IsNullWhenEverythingFits()
    {
        Assert.Null(TranscriptScrollbar.ThumbOf(10, 10, 0));
        Assert.Null(TranscriptScrollbar.ThumbOf(5, 10, 0));
        Assert.Null(TranscriptScrollbar.ThumbOf(5, 0, 0));
    }

    [Fact]
    public void ThumbOf_SitsAtTheBottomWhenFollowingAndAtTheTopWhenFullyScrolledBack()
    {
        Assert.Equal((5, 5), TranscriptScrollbar.ThumbOf(20, 10, 0));
        Assert.Equal((0, 5), TranscriptScrollbar.ThumbOf(20, 10, 10));
    }

    [Fact]
    public void ThumbOf_KeepsAtLeastOneRowAndClampsScroll()
    {
        Assert.Equal((9, 1), TranscriptScrollbar.ThumbOf(10_000, 10, 0));
        Assert.Equal((0, 1), TranscriptScrollbar.ThumbOf(10_000, 10, 99_999));
    }

    [Fact]
    public void Apply_ReservesTheLastColumnOnEveryRow()
    {
        var lines = new[] { PaintLine.Colored("red", "hello"), PaintLine.Blank, PaintLine.Blank };

        var fits = TranscriptScrollbar.Apply(lines, 10, 3, 0);
        var overflows = TranscriptScrollbar.Apply(lines, 10, 30, 0);

        Assert.All(fits, line => Assert.Equal(10, TextWidth.Measure(line.Plain)));
        Assert.All(fits, line => Assert.EndsWith(" ", line.Plain, StringComparison.Ordinal));
        Assert.All(overflows, line => Assert.Equal(10, TextWidth.Measure(line.Plain)));
        Assert.StartsWith("hello", overflows[0].Plain, StringComparison.Ordinal);
        Assert.EndsWith("│", overflows[0].Plain, StringComparison.Ordinal);
        Assert.EndsWith("┃", overflows[2].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UsesThemeColorsForTrackAndThumb()
    {
        try
        {
            Theme.TryApply(
                new Dictionary<string, string>
                {
                    ["status.scrollbar"] = "grey",
                    ["status.scrollbar-thumb"] = "orange1"
                },
                out _);
            var lines = new[] { PaintLine.Blank, PaintLine.Blank, PaintLine.Blank, PaintLine.Blank };

            var bar = TranscriptScrollbar.Apply(lines, 5, 40, 0);

            Assert.Contains("[grey]│[/]", bar[0].Markup, StringComparison.Ordinal);
            Assert.Contains("[orange1]┃[/]", bar[3].Markup, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }
}
