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
                lines.Add(ToLine(markup, plain));
                markup.Clear();
                plain.Clear();
                continue;
            }

            if (segment.Text.Length == 0)
            {
                continue;
            }

            plain.Append(segment.Text);
            var token = StyleToken(segment.Style);
            if (token.Length == 0)
            {
                markup.Append(MarkupText.Escape(segment.Text));
            }
            else
            {
                markup.Append('[').Append(token).Append(']')
                    .Append(MarkupText.Escape(segment.Text))
                    .Append("[/]");
            }
        }

        if (plain.Length > 0)
        {
            lines.Add(ToLine(markup, plain));
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
        var text = plain.ToString().TrimEnd();
        if (text.Length == 0)
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
