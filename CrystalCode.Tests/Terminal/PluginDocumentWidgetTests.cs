using CrystalCode.Display.Paint;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class PluginDocumentWidgetTests
{
    [Fact]
    public void Create_DrawsEveryBlockKindInsideAWidthBoundedCard()
    {
        var document = new TranscriptDocument(
            "Build [info]",
            [
                new("paragraph", [], [], ["All [green] checks ran."], 0),
                new("keyValues", [], [["branch", "main"], ["dirty", "no"]], [], 0),
                new("table", ["name", "ms"], [["compile", "120"], ["test [x]", "300"]], [], 0),
                new("list", [], [], ["one", "two"], 3)
            ]);

        var lines = WidgetPaint.Lines(PluginDocumentWidget.Create(document), 60);

        Assert.Contains(lines, line => line.Plain.Contains("Build [info]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("All [green] checks ran.", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("branch", StringComparison.Ordinal)
            && line.Plain.Contains("main", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("test [x]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("- two", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("(3 more omitted)", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains('╭'));
        Assert.All(lines, line => Assert.True(TextWidth.Measure(line.Plain) <= 60));
    }

    [Fact]
    public void Create_ShowsAnEmptyDocumentInsideTheCard()
    {
        var lines = WidgetPaint.Lines(PluginDocumentWidget.Create(new TranscriptDocument("Empty", [])), 60);

        Assert.Contains(lines, line => line.Plain.Contains("Nothing to show.", StringComparison.Ordinal));
    }
}
