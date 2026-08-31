using CrystalHarness.Display.Paint;

using Xunit;

namespace CrystalHarness.Display.Tests.Paint;

public sealed class TextWidthWrapTests
{
    [Fact]
    public void Wrap_BreaksOnBudgetAndKeepsParagraphs()
    {
        var lines = TextWidth.Wrap("hello world\n\nnext", 6);

        Assert.Equal("hello ", lines[0]);
        Assert.Equal("world", lines[1]);
        Assert.Equal(string.Empty, lines[2]);
        Assert.Equal("next", lines[3]);
    }

    [Fact]
    public void Truncate_AddsEllipsis()
    {
        Assert.Equal("hel...", TextWidth.Truncate("hello world", 6));
    }
}
