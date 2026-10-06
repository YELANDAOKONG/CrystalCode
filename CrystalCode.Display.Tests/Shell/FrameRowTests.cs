using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

using Spectre.Console;
using Spectre.Console.Rendering;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class FrameRowTests
{
    private static readonly IAnsiConsole Raster = FrameRow.CreateRaster();

    [Fact]
    public void SafeMarkup_ExpandsTabsAndStripsControls()
    {
        var markup = FrameRow.SafeMarkup(new PaintLine("[red]a\tb\u001bc\n[/]", "abc"));

        Assert.Equal("[red]a    bc[/]", markup);
    }

    [Fact]
    public void Create_KeepsARowThatFits()
    {
        var row = FrameRow.Create(PaintLine.Colored(Theme.Chrome, "hello"), 10, Raster);

        Assert.Equal(5, row.CellCount());
    }

    [Fact]
    public void Create_CropsInsteadOfWrappingWhenTheRowIsTooWide()
    {
        var plain = new string('x', 15) + " " + new string('y', 15);
        var row = FrameRow.Create(PaintLine.Colored(Theme.Chrome, plain), 20, Raster);

        Assert.Equal(20, row.CellCount());
        foreach (var segment in row.Render(default!, 20))
        {
            Assert.False(segment.IsLineBreak);
        }
    }

    [Fact]
    public void Create_CropsAtACellBoundaryForWideCharacters()
    {
        var row = FrameRow.Create(PaintLine.Colored(Theme.Chrome, "ab\u4f60\u597d"), 5, Raster);

        Assert.Equal(4, row.CellCount());
    }

    [Fact]
    public void Create_EmojiRowMeasuredByTheTableStaysOnOneRow()
    {
        const int Width = 20;
        var wrapped = MarkdownRenderer.Render("Status: " + string.Concat(Enumerable.Repeat("\u2705", 15)), Width);

        foreach (var line in wrapped)
        {
            var row = FrameRow.Create(line.Fit(Width), Width, Raster);

            Assert.True(row.CellCount() <= Width);
            Assert.True(TextWidth.Measure(line.Plain) <= Width);
        }
    }

    [Fact]
    public void Create_IsEmptyForBlankRows()
    {
        Assert.True(FrameRow.Create(PaintLine.Blank, 10, Raster).IsEmpty);
    }
}
