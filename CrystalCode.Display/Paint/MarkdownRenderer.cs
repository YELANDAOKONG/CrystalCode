using System.Text;

namespace CrystalCode.Display.Paint;

/// <summary>
/// Lightweight Markdown parser producing PaintLines for the transcript.
/// Supports headings, fenced code with language badges and diff highlights,
/// lists (ordered and unordered), blockquotes, horizontal rules, GFM tables,
/// and inline styling. A table that cannot be parsed or laid out is painted
/// as ordinary lines.
/// </summary>
public static class MarkdownRenderer
{
    private const string TableIndent = "  ";
    private const string TableGap = "  ";
    private const int TableMinColumnWidth = 4;

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
        var source = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var index = 0; index < source.Length; index++)
        {
            var raw = source[index];
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

            if (CanStartTable(raw) && TryConsumeTable(source, index, out var consumed))
            {
                if (!TryAppendTable(lines, source, index, consumed, width, scratch, markup))
                {
                    for (var offset = 0; offset < consumed; offset++)
                    {
                        RenderLooseLine(lines, source[index + offset], width, pieces, scratch, markup);
                    }
                }

                index += consumed - 1;
                continue;
            }

            RenderLooseLine(lines, raw, width, pieces, scratch, markup);
        }

        return lines;
    }

    private static void RenderLooseLine(
        List<PaintLine> lines,
        string raw,
        int width,
        List<InlinePiece> pieces,
        StringBuilder scratch,
        StringBuilder markup)
    {
        if (IsHorizontalRule(raw))
        {
            var ruleWidth = Math.Max(Math.Min(width - 4, 40), 10);
            var ruleText = "  " + new string('─', ruleWidth);
            lines.Add(PaintLine.Colored(Theme.Rule, ruleText));
            return;
        }

        if (TryHeading(raw, out var level, out var headingText))
        {
            RenderHeading(lines, level, headingText, width);
            return;
        }

        if (TryBlockquote(raw, out var quoteText))
        {
            RenderBlockquote(lines, quoteText, width, pieces, scratch, markup);
            return;
        }

        if (TryList(raw, out var listPrefix, out var itemText, out var isOrdered))
        {
            RenderListItem(lines, listPrefix, itemText, isOrdered, width, pieces, scratch, markup);
            return;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            lines.Add(PaintLine.Blank);
            return;
        }

        RenderParagraph(lines, raw, width, pieces, scratch, markup);
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

    private static bool CanStartTable(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || IsHorizontalRule(raw))
        {
            return false;
        }

        if (TryHeading(raw, out _, out _))
        {
            return false;
        }

        if (TryBlockquote(raw, out _))
        {
            return false;
        }

        return !TryList(raw, out _, out _, out _);
    }

    private static bool TryConsumeTable(string[] source, int start, out int count)
    {
        count = 0;
        if (start + 1 >= source.Length
            || !TrySplitRow(source[start], out _)
            || !TrySplitRow(source[start + 1], out var separator)
            || !IsDelimiterRow(separator))
        {
            return false;
        }

        var index = start + 2;
        while (index < source.Length && TrySplitRow(source[index], out _))
        {
            index++;
        }

        count = index - start;
        return true;
    }

    private static bool TryAppendTable(
        List<PaintLine> lines,
        string[] source,
        int start,
        int count,
        int width,
        StringBuilder scratch,
        StringBuilder markup)
    {
        try
        {
            if (!TryParseTable(source, start, count, scratch, out var table))
            {
                return false;
            }

            var rendered = new List<PaintLine>();
            if (!TryLayoutTable(rendered, table, width, markup))
            {
                return false;
            }

            lines.AddRange(rendered);
            return true;
        }
        catch (Exception)
        {
            // A broken table must not take down the transcript. The caller
            // paints the original lines instead.
            return false;
        }
    }

    private static bool TryParseTable(
        string[] source,
        int start,
        int count,
        StringBuilder scratch,
        out ParsedTable table)
    {
        table = default;
        if (count < 2
            || !TrySplitRow(source[start], out var headerCells)
            || !TrySplitRow(source[start + 1], out var separators)
            || separators.Count != headerCells.Count
            || headerCells.Count == 0)
        {
            return false;
        }

        var aligns = new ColumnAlign[headerCells.Count];
        for (var column = 0; column < separators.Count; column++)
        {
            if (!TryDelimiterAlign(separators[column], out aligns[column]))
            {
                return false;
            }
        }

        var header = new TableCell[headerCells.Count];
        for (var column = 0; column < headerCells.Count; column++)
        {
            if (!TryMakeCell(headerCells[column], scratch, out header[column]))
            {
                return false;
            }
        }

        var rows = new TableCell[count - 2][];
        for (var row = 0; row < rows.Length; row++)
        {
            if (!TrySplitRow(source[start + 2 + row], out var cells) || cells.Count != headerCells.Count)
            {
                return false;
            }

            var parsed = new TableCell[cells.Count];
            for (var column = 0; column < cells.Count; column++)
            {
                if (!TryMakeCell(cells[column], scratch, out parsed[column]))
                {
                    return false;
                }
            }

            rows[row] = parsed;
        }

        table = new ParsedTable(header, aligns, rows);
        return true;
    }

    private static bool TryMakeCell(string raw, StringBuilder scratch, out TableCell cell)
    {
        cell = default;
        if (TerminalText.HasControls(raw))
        {
            return false;
        }

        var pieces = new List<InlinePiece>();
        var text = ApplyInline(raw, pieces, scratch);
        if (TerminalText.HasControls(text) || !PiecesCover(text, pieces))
        {
            return false;
        }

        cell = new TableCell(text, pieces);
        return true;
    }

    private static bool PiecesCover(string text, List<InlinePiece> pieces)
    {
        var cursor = 0;
        foreach (var piece in pieces)
        {
            if (piece.Start != cursor || piece.End < piece.Start || piece.End > text.Length)
            {
                return false;
            }

            cursor = piece.End;
        }

        return cursor == text.Length;
    }

    private static bool TryLayoutTable(
        List<PaintLine> lines,
        ParsedTable table,
        int width,
        StringBuilder markup)
    {
        var columns = table.Header.Length;
        var budget = width - TextWidth.Measure(TableIndent);
        if (columns == 0 || budget < 1)
        {
            return false;
        }

        var gap = TextWidth.Measure(TableGap);
        var gaps = columns > 1 ? (columns - 1) * gap : 0;
        var inner = budget - gaps;
        if (inner < 1)
        {
            return TryAppendStacked(lines, table, budget, width, markup);
        }

        if (!TryAssignWidths(NaturalWidths(table), inner, out var widths, out var stack))
        {
            return false;
        }

        return stack
            ? TryAppendStacked(lines, table, budget, width, markup)
            : TryAppendAligned(lines, table, widths, width, markup);
    }

    private static int[] NaturalWidths(ParsedTable table)
    {
        var natural = new int[table.Header.Length];
        for (var column = 0; column < natural.Length; column++)
        {
            natural[column] = TextWidth.Measure(table.Header[column].Text);
            foreach (var row in table.Rows)
            {
                natural[column] = Math.Max(natural[column], TextWidth.Measure(row[column].Text));
            }
        }

        return natural;
    }

    private static bool TryAssignWidths(int[] natural, int inner, out int[] widths, out bool stack)
    {
        stack = false;
        widths = new int[natural.Length];
        if (natural.Length == 0 || inner < 1)
        {
            return false;
        }

        var used = 0;
        for (var column = 0; column < natural.Length; column++)
        {
            if (natural[column] < 0)
            {
                return false;
            }

            widths[column] = Math.Max(natural[column], 1);
            used += widths[column];
        }

        // One column must not absorb the whole overflow. Cutting the longest
        // cell to the floor wraps it into a ribbon and the row grows to that height.
        while (used > inner)
        {
            var band = WidestShrinkableBand(widths, natural);
            if (band.Count == 0)
            {
                stack = true;
                return true;
            }

            var cut = ShrinkBand(widths, natural, band, used - inner);
            if (cut < 1)
            {
                return false;
            }

            used -= cut;
        }

        foreach (var columnWidth in widths)
        {
            if (columnWidth < 1)
            {
                return false;
            }
        }

        return true;
    }

    private static int ShrinkFloor(int natural)
    {
        var least = Math.Max(natural, 1);
        return Math.Min(TableMinColumnWidth, least);
    }

    private static List<int> WidestShrinkableBand(int[] widths, int[] natural)
    {
        var band = new List<int>();
        var width = 0;
        for (var column = 0; column < widths.Length; column++)
        {
            if (widths[column] <= ShrinkFloor(natural[column]))
            {
                continue;
            }

            if (widths[column] > width)
            {
                band.Clear();
                width = widths[column];
            }

            if (widths[column] == width)
            {
                band.Add(column);
            }
        }

        return band;
    }

    private static int ShrinkBand(int[] widths, int[] natural, List<int> band, int overflow)
    {
        if (band.Count == 0 || overflow < 1)
        {
            return 0;
        }

        var bandWidth = widths[band[0]];
        var nextLower = 0;
        for (var column = 0; column < widths.Length; column++)
        {
            if (widths[column] < bandWidth && widths[column] > nextLower)
            {
                nextLower = widths[column];
            }
        }

        var room = int.MaxValue;
        foreach (var column in band)
        {
            var target = Math.Max(ShrinkFloor(natural[column]), nextLower);
            var columnRoom = widths[column] - target;
            if (columnRoom < 1)
            {
                return 0;
            }

            room = Math.Min(room, columnRoom);
        }

        var count = band.Count;
        if ((long)room * count <= overflow)
        {
            foreach (var column in band)
            {
                widths[column] -= room;
            }

            return room * count;
        }

        var each = overflow / count;
        var extra = overflow % count;
        var largestShare = extra > 0 ? each + 1 : each;
        if (largestShare < 1 || largestShare > room)
        {
            return 0;
        }

        var cut = 0;
        for (var index = 0; index < band.Count; index++)
        {
            var share = index < extra ? each + 1 : each;
            widths[band[index]] -= share;
            cut += share;
        }

        return cut;
    }

    private static bool TryAppendAligned(
        List<PaintLine> lines,
        ParsedTable table,
        int[] widths,
        int width,
        StringBuilder markup)
    {
        var plain = new StringBuilder();
        if (!TryAppendRow(lines, table.Header, widths, table.Aligns, heading: true, width, markup, plain)
            || !TryAppendRule(lines, widths, width, markup, plain))
        {
            return false;
        }

        foreach (var row in table.Rows)
        {
            if (!TryAppendRow(lines, row, widths, table.Aligns, heading: false, width, markup, plain))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendRow(
        List<PaintLine> lines,
        TableCell[] cells,
        int[] widths,
        ColumnAlign[] aligns,
        bool heading,
        int width,
        StringBuilder markup,
        StringBuilder plain)
    {
        var wrapped = new List<(int Start, int End)>[cells.Length];
        var height = 1;
        for (var column = 0; column < cells.Length; column++)
        {
            if (!TryWrapCell(cells[column].Text, widths[column], out var parts))
            {
                return false;
            }

            wrapped[column] = parts;
            height = Math.Max(height, parts.Count);
        }

        for (var line = 0; line < height; line++)
        {
            if (!TryEmitScreenRow(lines, cells, wrapped, line, widths, aligns, heading, width, markup, plain))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryEmitScreenRow(
        List<PaintLine> lines,
        TableCell[] cells,
        List<(int Start, int End)>[] wrapped,
        int line,
        int[] widths,
        ColumnAlign[] aligns,
        bool heading,
        int width,
        StringBuilder markup,
        StringBuilder plain)
    {
        plain.Clear();
        markup.Clear();
        plain.Append(TableIndent);
        markup.Append(TableIndent);
        for (var column = 0; column < cells.Length; column++)
        {
            if (column > 0)
            {
                plain.Append(TableGap);
                markup.Append(TableGap);
            }

            var start = 0;
            var end = 0;
            if (line < wrapped[column].Count)
            {
                start = wrapped[column][line].Start;
                end = wrapped[column][line].End;
            }

            var slice = cells[column].Text[start..end];
            var pad = widths[column] - TextWidth.Measure(slice);
            if (pad < 0 || widths[column] < 1)
            {
                return false;
            }

            var left = aligns[column] switch
            {
                ColumnAlign.Right => pad,
                ColumnAlign.Center => pad / 2,
                ColumnAlign.Left => 0,
                _ => 0
            };
            AppendSpaces(plain, markup, left);
            plain.Append(slice);
            if (heading)
            {
                AppendSliceMarkup(markup, cells[column].Text, start, end, heading: true, pieces: null);
            }
            else
            {
                AppendSliceMarkup(markup, cells[column].Text, start, end, heading: false, cells[column].Pieces);
            }

            AppendSpaces(plain, markup, pad - left);
        }

        if (TextWidth.Measure(plain.ToString()) > width)
        {
            return false;
        }

        lines.Add(new PaintLine(markup.ToString(), plain.ToString()));
        return true;
    }

    private static bool TryAppendRule(
        List<PaintLine> lines,
        int[] widths,
        int width,
        StringBuilder markup,
        StringBuilder plain)
    {
        plain.Clear();
        plain.Append(TableIndent);
        for (var column = 0; column < widths.Length; column++)
        {
            if (widths[column] < 1)
            {
                return false;
            }

            if (column > 0)
            {
                plain.Append(TableGap);
            }

            plain.Append('─', widths[column]);
        }

        if (TextWidth.Measure(plain.ToString()) > width)
        {
            return false;
        }

        markup.Clear();
        markup.Append('[').Append(Theme.Muted).Append(']');
        markup.Append(MarkupText.Escape(plain.ToString()));
        markup.Append("[/]");
        lines.Add(new PaintLine(markup.ToString(), plain.ToString()));
        return true;
    }

    private static bool TryAppendStacked(
        List<PaintLine> lines,
        ParsedTable table,
        int budget,
        int width,
        StringBuilder markup)
    {
        var plain = new StringBuilder();
        if (table.Rows.Length == 0)
        {
            foreach (var header in table.Header)
            {
                if (!TryAppendField(lines, header.Text, budget, width, heading: true, pieces: null, markup, plain))
                {
                    return false;
                }
            }

            return true;
        }

        for (var row = 0; row < table.Rows.Length; row++)
        {
            if (row > 0)
            {
                lines.Add(PaintLine.Blank);
            }

            for (var column = 0; column < table.Header.Length; column++)
            {
                if (!TryAppendField(lines, table.Header[column].Text, budget, width, heading: true, pieces: null, markup, plain)
                    || !TryAppendField(lines, table.Rows[row][column].Text, budget, width, heading: false, table.Rows[row][column].Pieces, markup, plain))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryAppendField(
        List<PaintLine> lines,
        string text,
        int budget,
        int width,
        bool heading,
        List<InlinePiece>? pieces,
        StringBuilder markup,
        StringBuilder plain)
    {
        if (text.Length == 0)
        {
            return true;
        }

        if (budget < 1 || !TryWrapCell(text, budget, out var parts))
        {
            return false;
        }

        foreach (var part in parts)
        {
            plain.Clear();
            markup.Clear();
            plain.Append(TableIndent);
            markup.Append(TableIndent);
            plain.Append(text, part.Start, part.End - part.Start);
            AppendSliceMarkup(markup, text, part.Start, part.End, heading, pieces);
            if (TextWidth.Measure(plain.ToString()) > width)
            {
                return false;
            }

            lines.Add(new PaintLine(markup.ToString(), plain.ToString()));
        }

        return true;
    }

    private static void AppendSliceMarkup(
        StringBuilder markup,
        string text,
        int start,
        int end,
        bool heading,
        List<InlinePiece>? pieces)
    {
        if (end <= start)
        {
            return;
        }

        if (heading || pieces is null)
        {
            markup.Append('[').Append(Theme.Heading).Append(']');
            markup.Append(MarkupText.Escape(text[start..end]));
            markup.Append("[/]");
            return;
        }

        AppendMarkup(markup, text, start, end, pieces);
    }

    private static bool TryWrapCell(string text, int columnWidth, out List<(int Start, int End)> parts)
    {
        parts = [];
        if (columnWidth < 1)
        {
            return false;
        }

        if (text.Length == 0)
        {
            parts.Add((0, 0));
            return true;
        }

        var start = 0;
        while (start < text.Length)
        {
            var end = TextWidth.FitEnd(text, start, text.Length, columnWidth);
            if (end <= start)
            {
                return false;
            }

            if (TextWidth.Measure(text.AsSpan(start, end - start)) > columnWidth)
            {
                return false;
            }

            parts.Add((start, end));
            start = end;
        }

        return true;
    }

    private static void AppendSpaces(StringBuilder plain, StringBuilder markup, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var spaces = new string(' ', count);
        plain.Append(spaces);
        markup.Append(spaces);
    }

    private static bool TrySplitRow(string line, out List<string> cells)
    {
        cells = [];
        var trimmed = line.Trim();
        if (!ContainsUnescapedPipe(trimmed))
        {
            return false;
        }

        var start = 0;
        var end = trimmed.Length;
        if (trimmed[0] == '|')
        {
            start = 1;
        }

        if (end > start && trimmed[end - 1] == '|' && !IsEscaped(trimmed, end - 1))
        {
            end--;
        }

        SplitCells(trimmed[start..end], cells);
        return cells.Count > 0;
    }

    private static void SplitCells(string body, List<string> cells)
    {
        var current = new StringBuilder();
        var inCode = false;
        for (var index = 0; index < body.Length; index++)
        {
            var ch = body[index];
            if (ch == '`')
            {
                inCode = !inCode;
                current.Append(ch);
                continue;
            }

            if (ch == '\\' && index + 1 < body.Length && body[index + 1] is '|' or '\\')
            {
                current.Append(body[index + 1]);
                index++;
                continue;
            }

            if (ch == '|' && !inCode)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        cells.Add(current.ToString().Trim());
    }

    private static bool ContainsUnescapedPipe(string text)
    {
        var inCode = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '`')
            {
                inCode = !inCode;
                continue;
            }

            if (ch == '\\' && index + 1 < text.Length && text[index + 1] is '|' or '\\')
            {
                index++;
                continue;
            }

            if (ch == '|' && !inCode)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEscaped(string text, int index)
    {
        var slashes = 0;
        for (var cursor = index - 1; cursor >= 0 && text[cursor] == '\\'; cursor--)
        {
            slashes++;
        }

        return slashes % 2 == 1;
    }

    private static bool IsDelimiterRow(List<string> cells)
    {
        if (cells.Count == 0)
        {
            return false;
        }

        foreach (var cell in cells)
        {
            if (!TryDelimiterAlign(cell, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDelimiterAlign(string cell, out ColumnAlign align)
    {
        align = ColumnAlign.Left;
        if (cell.Length == 0)
        {
            return false;
        }

        var left = cell[0] == ':';
        var right = cell[^1] == ':';
        var start = left ? 1 : 0;
        var end = right ? cell.Length - 1 : cell.Length;
        if (end <= start)
        {
            return false;
        }

        for (var index = start; index < end; index++)
        {
            if (cell[index] != '-')
            {
                return false;
            }
        }

        align = left && right
            ? ColumnAlign.Center
            : right
                ? ColumnAlign.Right
                : ColumnAlign.Left;
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

    private enum ColumnAlign
    {
        Left,
        Center,
        Right
    }

    private readonly record struct InlinePiece(int Start, int End, InlineKind Kind);

    private readonly record struct TableCell(string Text, List<InlinePiece> Pieces);

    private readonly record struct ParsedTable(
        TableCell[] Header,
        ColumnAlign[] Aligns,
        TableCell[][] Rows);
}
