using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

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

    [Fact]
    public void Colored_FlattensEscapeSequencesAndBreaks()
    {
        var line = PaintLine.Colored(Theme.Chrome, "a\u001b[2Jb\nc");

        Assert.Equal("ab c", line.Plain);
        Assert.False(TerminalText.HasControls(line.Markup));
    }

    [Fact]
    public void Colored_KeepsTabsWhileFlatteningBreaks()
    {
        var line = PaintLine.Colored(Theme.Chrome, "a\tb\nc");

        Assert.Equal("a\tb c", line.Plain);

        var fitted = line.Fit(20);
        Assert.Equal("a    b c", fitted.Plain);
        Assert.DoesNotContain('\t', fitted.Markup);
    }

    [Fact]
    public void Fit_StripsControlsFromHandBuiltRows()
    {
        var line = new PaintLine("[red]a\u001bb\n[/]", "a\u001bb\n").Fit(20);

        Assert.Equal("ab", line.Plain);
        Assert.Equal("[red]ab[/]", line.Markup);
    }
}
