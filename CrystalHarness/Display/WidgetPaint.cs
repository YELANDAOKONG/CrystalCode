using System.Text;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace CrystalHarness.Display;

/// <summary>
/// Turns a Spectre renderable into frame rows. Live is not used.
/// </summary>
public static class WidgetPaint
{
    public static IReadOnlyList<PaintLine> Lines(IRenderable renderable, int width)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        width = Math.Max(width, 16);
        var console = CreateConsole(width);
        var markup = new StringBuilder();
        var plain = new StringBuilder();
        var lines = new List<PaintLine>();
        foreach (var segment in renderable.GetSegments(console))
        {
            if (segment.IsControlCode)
            {
                continue;
            }

            if (segment.IsLineBreak)
            {
                FlushLine(lines, markup, plain, width);
                continue;
            }

            AppendSegment(lines, markup, plain, segment, width);
        }

        if (plain.Length > 0)
        {
            FlushLine(lines, markup, plain, width);
        }

        return lines;
    }

    public static IReadOnlyList<string> Plain(IRenderable renderable, int width)
    {
        var lines = new List<string>();
        foreach (var line in Lines(renderable, width))
        {
            lines.Add(line.Plain);
        }

        return lines;
    }

    private static void AppendSegment(
        List<PaintLine> lines,
        StringBuilder markup,
        StringBuilder plain,
        Segment segment,
        int width)
    {
        var text = segment.Text;
        if (text.Length == 0)
        {
            return;
        }

        var start = 0;
        while (start < text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline;
            if (end > start && text[end - 1] == '\r')
            {
                end--;
            }

            if (end > start)
            {
                AppendStyled(markup, plain, text[start..end], segment.Style);
            }

            if (newline < 0)
            {
                return;
            }

            FlushLine(lines, markup, plain, width);
            start = newline + 1;
        }
    }

    private static void AppendStyled(
        StringBuilder markup,
        StringBuilder plain,
        string text,
        Style style)
    {
        plain.Append(text);
        var token = StyleToken(style);
        if (token.Length == 0)
        {
            markup.Append(MarkupText.Escape(text));
            return;
        }

        markup.Append('[').Append(token).Append(']')
            .Append(MarkupText.Escape(text))
            .Append("[/]");
    }

    private static void FlushLine(
        List<PaintLine> lines,
        StringBuilder markup,
        StringBuilder plain,
        int width)
    {
        lines.Add(ToLine(markup, plain).Fit(width));
        markup.Clear();
        plain.Clear();
    }

    private static IAnsiConsole CreateConsole(int width)
    {
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.No,
                ColorSystem = ColorSystemSupport.Standard,
                Out = new AnsiConsoleOutput(TextWriter.Null)
            });
        console.Profile.Width = width;
        console.Profile.Height = 64;
        console.Profile.Capabilities.Unicode = true;
        return console;
    }

    private static PaintLine ToLine(StringBuilder markup, StringBuilder plain)
    {
        var text = plain.ToString();
        if (text.TrimEnd().Length == 0)
        {
            return PaintLine.Blank;
        }

        return new PaintLine(markup.ToString(), text);
    }

    private static string StyleToken(Style style)
    {
        var parts = new List<string>();
        if ((style.Decoration & Decoration.Bold) != 0)
        {
            parts.Add("bold");
        }

        if (style.Foreground != Color.Default)
        {
            parts.Add(style.Foreground.ToString().ToLowerInvariant());
        }

        return string.Join(' ', parts);
    }
}
