using System.Text;

namespace CrystalHarness.Display;

/// <summary>
/// Terminal markdown for assistant messages. Headings, lists, fences, inline.
/// </summary>
public static class MarkdownRenderer
{
    public static IReadOnlyList<PaintLine> Render(string markdown, int width)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        width = Math.Max(width, 8);
        var lines = new List<PaintLine>();
        var fenced = false;
        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                AddWrapped(lines, "    " + raw, Theme.Code, width);
                continue;
            }

            if (raw.Length == 0)
            {
                lines.Add(PaintLine.Blank);
                continue;
            }

            if (TryHeading(raw, out var title))
            {
                AddWrapped(lines, "  " + title, Theme.Heading, width);
                continue;
            }

            if (TryList(raw, out var item))
            {
                AddWrapped(lines, "  " + item, Theme.User, width);
                continue;
            }

            foreach (var wrapped in TextWidth.Wrap("  " + raw, width))
            {
                lines.Add(new PaintLine(InlineMarkup(wrapped), wrapped));
            }
        }

        return lines;
    }

    private static void AddWrapped(
        List<PaintLine> lines,
        string plain,
        string color,
        int width)
    {
        foreach (var wrapped in TextWidth.Wrap(plain, width))
        {
            lines.Add(PaintLine.Colored(color, wrapped));
        }
    }

    private static bool TryHeading(string raw, out string title)
    {
        title = string.Empty;
        if (raw[0] != '#')
        {
            return false;
        }

        var level = 0;
        while (level < raw.Length && raw[level] == '#' && level < 6)
        {
            level++;
        }

        if (level == 0 || level >= raw.Length || raw[level] != ' ')
        {
            return false;
        }

        title = raw[(level + 1)..].Trim();
        return title.Length > 0;
    }

    private static bool TryList(string raw, out string item)
    {
        item = string.Empty;
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            item = "* " + trimmed[2..].Trim();
            return true;
        }

        var dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dot <= 0)
        {
            return false;
        }

        for (var i = 0; i < dot; i++)
        {
            if (!char.IsDigit(trimmed[i]))
            {
                return false;
            }
        }

        item = trimmed;
        return true;
    }

    private static string InlineMarkup(string plain)
    {
        var markup = new StringBuilder();
        var i = 0;
        while (i < plain.Length)
        {
            if (plain[i] == '`' && TryTakeDelimited(plain, i, "`", out var code, out var afterCode))
            {
                markup.Append('[').Append(Theme.Code).Append(']')
                    .Append(MarkupText.Escape(code))
                    .Append("[/]");
                i = afterCode;
                continue;
            }

            if (plain[i] == '*'
                && i + 1 < plain.Length
                && plain[i + 1] == '*'
                && TryTakeDelimited(plain, i, "**", out var bold, out var afterBold))
            {
                markup.Append("[bold]").Append(MarkupText.Escape(bold)).Append("[/]");
                i = afterBold;
                continue;
            }

            var next = NextMarker(plain, i);
            markup.Append(MarkupText.Escape(plain[i..next]));
            i = next;
        }

        return markup.ToString();
    }

    private static int NextMarker(string plain, int start)
    {
        for (var i = start + 1; i < plain.Length; i++)
        {
            if (plain[i] is '`' or '*')
            {
                return i;
            }
        }

        return plain.Length;
    }

    private static bool TryTakeDelimited(
        string plain,
        int start,
        string delimiter,
        out string inner,
        out int after)
    {
        inner = string.Empty;
        after = start;
        if (start + delimiter.Length >= plain.Length)
        {
            return false;
        }

        var close = plain.IndexOf(delimiter, start + delimiter.Length, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        inner = plain[(start + delimiter.Length)..close];
        if (inner.Length == 0)
        {
            return false;
        }

        after = close + delimiter.Length;
        return true;
    }
}
