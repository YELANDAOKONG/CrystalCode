using Spectre.Console;

using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

public sealed class WidgetPaintTests
{
    [Fact]
    public void Lines_KeepsPanelTextAndIndent()
    {
        var panel = new Panel("hello")
        {
            Header = new PanelHeader("Card"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0)
        };
        var padded = new Padder(panel, new Padding(2, 0, 0, 0));

        var lines = WidgetPaint.Lines(padded, 40);

        Assert.Contains(lines, line => line.Plain.Contains("hello", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("Card", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.StartsWith("  ", StringComparison.Ordinal));
    }

    [Fact]
    public void Lines_ExpandedPaddedPanelFitsWidth()
    {
        const int width = 48;
        var panel = new Panel("查看其他问题")
        {
            Header = new PanelHeader("Queued Follow-up"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        var padded = new Padder(panel, new Padding(2, 0, 0, 0));

        var lines = WidgetPaint.Lines(padded, width);

        Assert.True(lines.Count >= 3);
        foreach (var line in lines)
        {
            Assert.DoesNotContain('\n', line.Plain);
            var measured = TextWidth.Measure(line.Plain);
            Assert.True(measured <= width, $"line width {measured} > {width}: '{line.Plain}'");
        }
    }
}
