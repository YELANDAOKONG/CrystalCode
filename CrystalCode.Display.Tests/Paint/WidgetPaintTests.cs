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

    [Fact]
    public void Lines_RemoveEscapeSequencesFromWidgetText()
    {
        var panel = new Panel(new Markup(MarkupText.Escape("body \u001b[2J\u001b]52;c;QUJD\u0007 end")))
        {
            Header = new PanelHeader(MarkupText.Escape("Bash  echo safe\u001b[1G\u001b[2Kls")),
            Border = BoxBorder.Rounded,
            Expand = true
        };

        var lines = WidgetPaint.Lines(new Padder(panel, new Padding(2, 0, 0, 0)), 60);

        foreach (var line in lines)
        {
            Assert.False(TerminalText.HasControls(line.Markup));
            Assert.False(TerminalText.HasControls(line.Plain));
        }

        Assert.Contains("body  end", lines[1].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_KeepDecorationsAndBackground()
    {
        var markup = new Markup("[italic dim underline strikethrough invert on red]abc[/] [bold blue]def[/]");

        var line = Assert.Single(WidgetPaint.Lines(markup, 40));

        Assert.Equal("abc def", line.Plain);
        Assert.Contains("bold", line.Markup, StringComparison.Ordinal);
        Assert.Contains("dim", line.Markup, StringComparison.Ordinal);
        Assert.Contains("italic", line.Markup, StringComparison.Ordinal);
        Assert.Contains("underline", line.Markup, StringComparison.Ordinal);
        Assert.Contains("strikethrough", line.Markup, StringComparison.Ordinal);
        Assert.Contains("invert", line.Markup, StringComparison.Ordinal);
        Assert.Contains("on red", line.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[#ff8800]x[/]")]
    [InlineData("[rgb(10,20,30) on #102030]x[/]")]
    [InlineData("[grey50]x[/]")]
    [InlineData("[orange1]x[/]")]
    public void StyleToken_RoundTripsThroughMarkup(string source)
    {
        var original = Assert.Single(WidgetPaint.Lines(new Markup(source), 20));
        var again = Assert.Single(WidgetPaint.Lines(new Markup(original.Markup), 20));

        Assert.Equal(original.Markup, again.Markup);
        Assert.NotEqual("x", original.Markup);
    }

    [Fact]
    public void Lines_DropLinksAndBlinking()
    {
        var markup = new Markup("[link=https://example.test slowblink]x[/]");

        var line = Assert.Single(WidgetPaint.Lines(markup, 20));

        Assert.DoesNotContain("link", line.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("blink", line.Markup, StringComparison.Ordinal);
    }
}
