using System.Text;

namespace CrystalCode.Display.Paint;

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
        var pieces = new List<InlinePiece>();
        var scratch = new StringBuilder();
        var markup = new StringBuilder();
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
                RenderBlockquote(lines, quoteText, width, pieces, scratch, markup);
                continue;
            }

            if (TryList(raw, out var listPrefix, out var itemText, out var isOrdered))
            {
                RenderListItem(lines, listPrefix, itemText, isOrdered, width, pieces, scratch, markup);
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                lines.Add(PaintLine.Blank);
                continue;
            }

            RenderParagraph(lines, raw, width, pieces, scratch, markup);
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
        int width,
        List<InlinePiece> pieces,
        StringBuilder scratch,
        StringBuilder markup)
    {
        var body = ApplyInline(text, pieces, scratch);
        var availWidth = Math.Max(width - 6, 8);
        if (body.Length == 0)
        {
            lines.Add(new PaintLine($"  [{Theme.Muted}]│[/]", "  │ "));
            return;
        }

        var start = 0;
        while (start < body.Length)
        {
            var end = NextWrap(body, start, availWidth);
            var plain = "  │ " + body[start..end];
            markup.Clear();
            markup.Append("  [").Append(Theme.Muted).Append("]│[/] [").Append(Theme.Chrome).Append(']');
            AppendMarkup(markup, body, start, end, pieces);
            markup.Append("[/]");
            lines.Add(new PaintLine(markup.ToString(), plain));
            start = end;
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
        ArgumentNullException.ThrowIfNull(plain);
        var pieces = new List<InlinePiece>();
        var visible = ApplyInline(plain, pieces, new StringBuilder());
        var markup = new StringBuilder();
        AppendMarkup(markup, visible, 0, visible.Length, pieces);
        return markup.ToString();
    }

    private static void RenderListItem(
        List<PaintLine> lines,
        string prefix,
        string text,
        bool isOrdered,
        int width,
        List<InlinePiece> pieces,
        StringBuilder scratch,
        StringBuilder markup)
    {
        var indent = isOrdered ? prefix.Length + 3 : 4;
        var availWidth = Math.Max(width - indent, 8);
        var body = ApplyInline(text, pieces, scratch);
        if (body.Length == 0)
        {
            lines.Add(ListLine(isOrdered, prefix, indent, string.Empty, 0, 0, pieces, markup, first: true));
            return;
        }

        var start = 0;
        var first = true;
        while (start < body.Length)
        {
            var end = NextWrap(body, start, availWidth);
            lines.Add(ListLine(isOrdered, prefix, indent, body, start, end, pieces, markup, first));
            first = false;
            start = end;
        }
    }

    private static PaintLine ListLine(
        bool isOrdered,
        string prefix,
        int indent,
        string body,
        int start,
        int end,
        List<InlinePiece> pieces,
        StringBuilder markup,
        bool first)
    {
        var slice = body[start..end];
        markup.Clear();
        if (first)
        {
            var bullet = isOrdered ? prefix : "*";
            var bulletMarkup = isOrdered
                ? $"[{Theme.Accent}]{bullet}[/]"
                : $"[{Theme.Muted}]*[/]";
            markup.Append("  ").Append(bulletMarkup).Append(' ');
            AppendMarkup(markup, body, start, end, pieces);
            return new PaintLine(markup.ToString(), "  " + bullet + " " + slice);
        }

        var pad = new string(' ', indent);
        markup.Append(pad);
        AppendMarkup(markup, body, start, end, pieces);
        return new PaintLine(markup.ToString(), pad + slice);
    }

    private static void RenderParagraph(
        List<PaintLine> lines,
        string raw,
        int width,
        List<InlinePiece> pieces,
        StringBuilder scratch,
        StringBuilder markup)
    {
        var trimmed = raw.Trim();
        var body = ApplyInline(trimmed, pieces, scratch);
        var plain = "  " + body;
        if (pieces.Count == 1 && pieces[0].Kind == InlineKind.Plain)
        {
            pieces[0] = new InlinePiece(0, plain.Length, InlineKind.Plain);
        }
        else
        {
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                pieces[i] = piece with { Start = piece.Start + 2, End = piece.End + 2 };
            }

            pieces.Insert(0, new InlinePiece(0, 2, InlineKind.Plain));
        }

        AppendWrapped(lines, plain, width, pieces, markup);
    }

    private static void AppendWrapped(
        List<PaintLine> lines,
        string plain,
        int width,
        List<InlinePiece> pieces,
        StringBuilder markup)
    {
        if (plain.Length == 0)
        {
            lines.Add(PaintLine.Blank);
            return;
        }

        var start = 0;
        while (start < plain.Length)
        {
            var end = NextWrap(plain, start, width);
            markup.Clear();
            AppendMarkup(markup, plain, start, end, pieces);
            lines.Add(new PaintLine(markup.ToString(), plain[start..end]));
            start = end;
        }
    }

    private static int NextWrap(string text, int start, int width)
    {
        var end = TextWidth.FitEnd(text, start, text.Length, width);
        if (end <= start)
        {
            end = TextWidth.MoveRight(text, start);
        }

        return end;
    }

    private static string ApplyInline(string text, List<InlinePiece> pieces, StringBuilder visible)
    {
        pieces.Clear();
        if (!ContainsMarker(text))
        {
            pieces.Add(new InlinePiece(0, text.Length, InlineKind.Plain));
            return text;
        }

        visible.Clear();
        var index = 0;
        while (index < text.Length)
        {
            if (TryStyle(text, index, out var kind, out var inner, out var after))
            {
                var start = visible.Length;
                visible.Append(inner);
                if (visible.Length > start)
                {
                    pieces.Add(new InlinePiece(start, visible.Length, kind));
                }

                index = after;
                continue;
            }

            var next = NextMarker(text, index);
            var plainStart = visible.Length;
            visible.Append(text, index, next - index);
            if (visible.Length > plainStart)
            {
                pieces.Add(new InlinePiece(plainStart, visible.Length, InlineKind.Plain));
            }

            index = next;
        }

        return visible.ToString();
    }

    private static void AppendMarkup(
        StringBuilder markup,
        string text,
        int start,
        int end,
        List<InlinePiece> pieces)
    {
        foreach (var piece in pieces)
        {
            if (piece.End <= start)
            {
                continue;
            }

            if (piece.Start >= end)
            {
                break;
            }

            var from = Math.Max(piece.Start, start);
            var to = Math.Min(piece.End, end);
            if (from >= to)
            {
                continue;
            }

            var slice = text[from..to];
            if (piece.Kind == InlineKind.Plain)
            {
                markup.Append(MarkupText.Escape(slice));
                continue;
            }

            markup.Append(piece.Kind switch
            {
                InlineKind.Code => $"[{Theme.Code}]",
                InlineKind.Bold => "[bold]",
                InlineKind.Italic => "[italic]",
                InlineKind.Strike => "[strikethrough]",
                _ => string.Empty
            });
            markup.Append(MarkupText.Escape(slice));
            markup.Append("[/]");
        }
    }

    private static bool ContainsMarker(string text)
    {
        foreach (var ch in text)
        {
            if (ch is '`' or '*' or '~')
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryStyle(
        string text,
        int start,
        out InlineKind kind,
        out string inner,
        out int after)
    {
        kind = InlineKind.Plain;
        inner = string.Empty;
        after = start;
        if (text[start] == '`' && TryTakeDelimited(text, start, "`", out inner, out after))
        {
            kind = InlineKind.Code;
            return true;
        }

        if (text[start] == '*'
            && start + 1 < text.Length
            && text[start + 1] == '*'
            && TryTakeDelimited(text, start, "**", out inner, out after))
        {
            kind = InlineKind.Bold;
            return true;
        }

        if (text[start] == '*'
            && (start + 1 >= text.Length || text[start + 1] != '*')
            && TryTakeDelimited(text, start, "*", out inner, out after))
        {
            kind = InlineKind.Italic;
            return true;
        }

        if (text[start] == '~'
            && start + 1 < text.Length
            && text[start + 1] == '~'
            && TryTakeDelimited(text, start, "~~", out inner, out after))
        {
            kind = InlineKind.Strike;
            return true;
        }

        return false;
    }

    private static int NextMarker(string text, int start)
    {
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] is '`' or '*' or '~')
            {
                return i;
            }
        }

        return text.Length;
    }

    private static bool TryTakeDelimited(
        string text,
        int start,
        string delimiter,
        out string inner,
        out int after)
    {
        inner = string.Empty;
        after = start;
        if (start + delimiter.Length >= text.Length)
        {
            return false;
        }

        var close = text.IndexOf(delimiter, start + delimiter.Length, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        inner = text[(start + delimiter.Length)..close];
        after = close + delimiter.Length;
        return true;
    }

    private enum InlineKind
    {
        Plain,
        Code,
        Bold,
        Italic,
        Strike
    }

    private readonly record struct InlinePiece(int Start, int End, InlineKind Kind);
}
