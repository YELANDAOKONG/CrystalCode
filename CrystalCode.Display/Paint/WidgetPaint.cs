using System.Text;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace CrystalCode.Display.Paint;

/// <summary>
/// Turns a Spectre renderable into frame rows. Live is not used.
/// </summary>
public static class WidgetPaint
{
    public const int MinimumWidth = 16;

    public static IReadOnlyList<PaintLine> Lines(IRenderable renderable, int width)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        width = Math.Max(width, MinimumWidth);
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
        lines.Add(RasterLine(markup.ToString(), plain.ToString(), width));
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

    private static PaintLine RasterLine(string markup, string plain, int width)
    {
        if (!plain.Contains('\t') && !markup.Contains('\t'))
        {
            return ToLine(markup, plain).Fit(width);
        }

        // Spectre sizes a tab as one cell. Painted rows use four columns, so the
        // extra width has to come back out of the panel padding or the border is clipped.
        markup = TextWidth.ExpandTabs(markup);
        plain = TextWidth.ExpandTabs(plain);
        var measured = TextWidth.Measure(plain);
        if (measured > width
            && TryReclaimPadding(ref markup, ref plain, measured - width))
        {
            measured = TextWidth.Measure(plain);
        }

        if (measured <= width)
        {
            return ToLine(markup, plain);
        }

        return new PaintLine(markup, plain).Fit(width);
    }

    private static bool TryReclaimPadding(ref string markup, ref string plain, int overflow)
    {
        if (overflow < 1)
        {
            return true;
        }

        if (!TryTrimPlainPadding(plain, overflow, out var nextPlain)
            || !TryTrimMarkupPadding(markup, overflow, out var nextMarkup))
        {
            return false;
        }

        plain = nextPlain;
        markup = nextMarkup;
        return true;
    }

    private static bool TryTrimPlainPadding(string plain, int overflow, out string result)
    {
        result = plain;
        var border = plain.Length - 1;
        while (border >= 0 && plain[border] == ' ')
        {
            border--;
        }

        if (border <= 0)
        {
            return false;
        }

        var spaces = 0;
        for (var index = border - 1; index >= 0 && plain[index] == ' '; index--)
        {
            spaces++;
        }

        if (spaces < overflow)
        {
            return false;
        }

        result = plain.Remove(border - overflow, overflow);
        return true;
    }

    private static bool TryTrimMarkupPadding(string markup, int overflow, out string result)
    {
        result = markup;
        if (!TryLastLiteralSpaceRun(markup, out var start, out var length) || length < overflow)
        {
            return false;
        }

        result = markup.Remove(start + length - overflow, overflow);
        return true;
    }

    private static bool TryLastLiteralSpaceRun(string markup, out int start, out int length)
    {
        var bestStart = -1;
        var bestLength = 0;
        var runStart = -1;
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
                    CloseRun(index);
                    index += 2;
                    continue;
                }

                CloseRun(index);
                inTag = true;
                index++;
                continue;
            }

            if (ch == ' ')
            {
                if (runStart < 0)
                {
                    runStart = index;
                }

                index++;
                continue;
            }

            CloseRun(index);
            index++;
        }

        CloseRun(markup.Length);
        start = bestStart;
        length = bestLength;
        return start >= 0;

        void CloseRun(int end)
        {
            if (runStart < 0)
            {
                return;
            }

            var run = end - runStart;
            if (run > 0)
            {
                bestStart = runStart;
                bestLength = run;
            }

            runStart = -1;
        }
    }

    private static PaintLine ToLine(string markup, string plain)
    {
        if (plain.TrimEnd().Length == 0)
        {
            return PaintLine.Blank;
        }

        return new PaintLine(markup, plain);
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
