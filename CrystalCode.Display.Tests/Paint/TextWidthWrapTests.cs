using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

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

    [Fact]
    public void Measure_CountsFormatAsZeroAndEmojiAsTwoColumns()
    {
        Assert.Equal(0, TextWidth.Measure("\u200d"));
        Assert.Equal(2, TextWidth.Measure("\U0001F680"));
        Assert.Equal(2, TextWidth.Measure("你"));
    }

    [Fact]
    public void Wrap_CountsTabsAsFourDisplayColumns()
    {
        Assert.Equal(6, TextWidth.Measure("a\tb"));
        Assert.Equal(["a\t", "b"], TextWidth.Wrap("a\tb", 5));
    }

    [Fact]
    public void PaintLine_FitExpandsTabsInPlainAndMarkup()
    {
        var line = PaintLine.Colored(Theme.Chrome, "a\tb").Fit(10);

        Assert.Equal("a    b", line.Plain);
        Assert.DoesNotContain('\t', line.Markup);
    }
}
