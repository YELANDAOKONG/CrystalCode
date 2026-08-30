using Spectre.Console;

using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

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
}
