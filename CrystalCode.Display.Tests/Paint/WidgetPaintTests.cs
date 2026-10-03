using System.Text;

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

    [Fact]
    public void Lines_TabInPanelKeepsTheBorder()
    {
        const int width = 40;
        var panel = new Panel(new Markup("[grey84]col1\tcol2[/]"))
        {
            Header = new PanelHeader("Side"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Rule),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };

        var lines = WidgetPaint.Lines(panel, width);
        var body = lines.Single(line => line.Plain.Contains("col1", StringComparison.Ordinal));

        Assert.Contains("col1    col2", body.Plain, StringComparison.Ordinal);
        Assert.EndsWith("│", body.Plain.TrimEnd());
        Assert.DoesNotContain("...", body.Plain);
        Assert.Contains("grey84", body.Markup, StringComparison.Ordinal);
        Assert.Equal(body.Plain, VisibleMarkup(body.Markup));
        Assert.All(lines, line =>
        {
            Assert.True(TextWidth.Measure(line.Plain) <= width);
            Assert.Equal(line.Plain, VisibleMarkup(line.Markup));
        });
    }

    private static string VisibleMarkup(string markup)
    {
        var text = new StringBuilder();
        var inTag = false;
        var index = 0;
        while (index < markup.Length)
        {
            var ch = markup[index];
            if (inTag)
            {
                if (ch == ']')
                {
                    inTag = false;
                }

                index++;
                continue;
            }

            if (ch == '[')
            {
                if (index + 1 < markup.Length && markup[index + 1] == '[')
                {
                    text.Append('[');
                    index += 2;
                    continue;
                }

                inTag = true;
                index++;
                continue;
            }

            if (ch == ']' && index + 1 < markup.Length && markup[index + 1] == ']')
            {
                text.Append(']');
                index += 2;
                continue;
            }

            text.Append(ch);
            index++;
        }

        return text.ToString();
    }
}
