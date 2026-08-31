using System.Text;

namespace CrystalHarness.Display.Paint;

/// <summary>
/// Lightweight Markdown parser producing PaintLines for the transcript.
/// Supports headings, fenced code with language badges and diff highlights,
/// lists (ordered and unordered), blockquotes, horizontal rules, and inline styling.
/// </summary>
public static class MarkdownRenderer
{
    public static IReadOnlyList<PaintLine> Render(string markdown, int width)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length == 0)
        {
            return [];
        }

        var lines = new List<PaintLine>();
        var inFence = false;
        string? fenceLang = null;

        foreach (var raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceLang = raw.Length > 3 ? raw[3..].Trim() : null;
                    if (!string.IsNullOrEmpty(fenceLang))
                    {
                        var badge = $"── {fenceLang} ──";
                        lines.Add(PaintLine.Colored(Theme.Muted, "    " + badge));
                    }
                }
                else
                {
                    inFence = false;
                    fenceLang = null;
                }

                continue;
            }

            if (inFence)
            {
                RenderFencedLine(lines, raw, fenceLang, width);
                continue;
            }

            if (IsHorizontalRule(raw))
            {
                var ruleWidth = Math.Max(Math.Min(width - 4, 40), 10);
                var ruleText = "  " + new string('─', ruleWidth);
                lines.Add(PaintLine.Colored(Theme.Rule, ruleText));
                continue;
            }

            if (TryHeading(raw, out var level, out var headingText))
            {
                RenderHeading(lines, level, headingText, width);
                continue;
            }

            if (TryBlockquote(raw, out var quoteText))
            {
                RenderBlockquote(lines, quoteText, width);
                continue;
            }

            if (TryList(raw, out var listPrefix, out var itemText, out var isOrdered))
            {
                RenderListItem(lines, listPrefix, itemText, isOrdered, width);
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                lines.Add(PaintLine.Blank);
                continue;
            }

            RenderParagraph(lines, raw, width);
        }

        return lines;
    }

    private static void RenderHeading(
        List<PaintLine> lines,
        int level,
        string text,
        int width)
    {
        var prefix = level switch
        {
            1 => "# ",
            2 => "## ",
            3 => "### ",
            _ => "#### "
        };

        var style = level switch
        {
            1 => $"[{Theme.Heading} underline]",
            2 => $"[{Theme.Heading}]",
            _ => $"[{Theme.Accent}]"
        };

        var plain = "  " + prefix + text;
        foreach (var wrapped in TextWidth.Wrap(plain, width))
        {
            var markup = style + MarkupText.Escape(wrapped) + "[/]";
            lines.Add(new PaintLine(markup, wrapped));
        }
    }

    private static void RenderBlockquote(
        List<PaintLine> lines,
        string text,
        int width)
    {
        var availWidth = Math.Max(width - 6, 8);
        var innerLines = TextWidth.Wrap(text, availWidth);
        foreach (var inner in innerLines)
        {
            var plain = "  │ " + inner;
            var markup = $"  [{Theme.Muted}]│[/] [{Theme.Chrome}]{InlineMarkup(inner)}[/]";
            lines.Add(new PaintLine(markup, plain));
        }
    }

    private static void RenderFencedLine(
        List<PaintLine> lines,
        string raw,
        string? fenceLang,
        int width)
    {
        var isDiff = fenceLang is "diff" or "patch"
            || (raw.Length > 0 && (raw[0] is '+' or '-' or '@'));

        if (isDiff && raw.Length > 0)
        {
            if (raw.StartsWith('+') && !raw.StartsWith("+++", StringComparison.Ordinal))
            {
                AddWrapped(lines, "    " + raw, Theme.DiffAdded, width);
                return;
            }

            if (raw.StartsWith('-') && !raw.StartsWith("---", StringComparison.Ordinal))
            {
                AddWrapped(lines, "    " + raw, Theme.DiffRemoved, width);
                return;
            }

            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                AddWrapped(lines, "    " + raw, Theme.Accent, width);
                return;
            }
        }

        AddWrapped(lines, "    " + raw, Theme.Code, width, onBackground: Theme.CodeBg);
    }

    private static void AddWrapped(
        List<PaintLine> lines,
        string plain,
        string color,
        int width,
        string? onBackground = null)
    {
        foreach (var wrapped in TextWidth.Wrap(plain, width))
        {
            if (string.IsNullOrEmpty(onBackground))
            {
                lines.Add(PaintLine.Colored(color, wrapped));
                continue;
            }

            var markup = $"[{color} on {onBackground}]{MarkupText.Escape(wrapped)}[/]";
            lines.Add(new PaintLine(markup, wrapped));
        }
    }

    private static bool IsHorizontalRule(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        if (trimmed.All(ch => ch == '-') || trimmed.All(ch => ch == '*') || trimmed.All(ch => ch == '_'))
        {
            return true;
        }

        return false;
    }

    private static bool TryHeading(string raw, out int level, out string title)
    {
        level = 0;
        title = string.Empty;
        var trimmed = raw.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#')
        {
            return false;
        }

        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }

        if (level > 6 || level >= trimmed.Length || trimmed[level] != ' ')
        {
            level = 0;
            return false;
        }

        title = trimmed[(level + 1)..].Trim();
        return true;
    }

    private static bool TryBlockquote(string raw, out string text)
    {
        text = string.Empty;
        var trimmed = raw.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '>')
        {
            text = trimmed.Length > 1 && trimmed[1] == ' ' ? trimmed[2..] : trimmed[1..];
            return true;
        }

        return false;
    }

    private static bool TryList(
        string raw,
        out string prefix,
        out string item,
        out bool isOrdered)
    {
        prefix = string.Empty;
        item = string.Empty;
        isOrdered = false;

        var trimmed = raw.TrimStart();
        if (trimmed.Length >= 2 && (trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("* ", StringComparison.Ordinal)
            || trimmed.StartsWith("+ ", StringComparison.Ordinal)))
        {
            prefix = "* ";
            item = trimmed[2..].Trim();
            return true;
        }

        var dot = trimmed.IndexOf(". ", StringComparison.Ordinal);
        if (dot <= 0 || dot > 4)
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

        prefix = trimmed[..dot] + ".";
        item = trimmed[(dot + 2)..].Trim();
        isOrdered = true;
        return true;
    }

    public static string InlineMarkup(string plain)
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

            if (plain[i] == '*'
                && (i + 1 >= plain.Length || plain[i + 1] != '*')
                && TryTakeDelimited(plain, i, "*", out var italicStar, out var afterItalicStar))
            {
                markup.Append("[italic]").Append(MarkupText.Escape(italicStar)).Append("[/]");
                i = afterItalicStar;
                continue;
            }

            if (plain[i] == '~'
                && i + 1 < plain.Length
                && plain[i + 1] == '~'
                && TryTakeDelimited(plain, i, "~~", out var strike, out var afterStrike))
            {
                markup.Append("[strikethrough]").Append(MarkupText.Escape(strike)).Append("[/]");
                i = afterStrike;
                continue;
            }

            if (plain[i] == '_'
                && i + 1 < plain.Length
                && plain[i + 1] == '_'
                && TryTakeDelimited(plain, i, "__", out var underline, out var afterUnderline))
            {
                markup.Append("[underline]").Append(MarkupText.Escape(underline)).Append("[/]");
                i = afterUnderline;
                continue;
            }

            if (plain[i] == '_'
                && (i + 1 >= plain.Length || plain[i + 1] != '_')
                && TryTakeDelimited(plain, i, "_", out var italicUnder, out var afterItalicUnder))
            {
                markup.Append("[italic]").Append(MarkupText.Escape(italicUnder)).Append("[/]");
                i = afterItalicUnder;
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
            if (plain[i] is '`' or '*' or '~' or '_')
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
        after = close + delimiter.Length;
        return true;
    }

    private static void RenderListItem(
        List<PaintLine> lines,
        string prefix,
        string text,
        bool isOrdered,
        int width)
    {
        var indent = isOrdered ? prefix.Length + 3 : 4;
        var availWidth = Math.Max(width - indent, 8);
        var bodyLines = TextWidth.Wrap(text, availWidth);
        for (var i = 0; i < bodyLines.Count; i++)
        {
            var body = bodyLines[i];
            if (i == 0)
            {
                var bullet = isOrdered ? prefix : "*";
                var plain = "  " + bullet + " " + body;
                var bulletMarkup = isOrdered
                    ? $"[{Theme.Accent}]{bullet}[/]"
                    : $"[{Theme.Muted}]*[/]";
                var markup = "  " + bulletMarkup + " " + InlineMarkup(body);
                lines.Add(new PaintLine(markup, plain));
            }
            else
            {
                var pad = new string(' ', indent);
                var plain = pad + body;
                var markup = pad + InlineMarkup(body);
                lines.Add(new PaintLine(markup, plain));
            }
        }
    }

    private static void RenderParagraph(
        List<PaintLine> lines,
        string raw,
        int width)
    {
        var plain = "  " + raw.Trim();
        foreach (var wrapped in TextWidth.Wrap(plain, width))
        {
            lines.Add(new PaintLine(InlineMarkup(wrapped), wrapped));
        }
    }
}
