using System.Text;
using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Transcript;

/// <summary>
/// Rounded transcript card as frame rows. Wrapping matches the Spectre panel
/// so a live block can keep finished rows instead of rasterizing the whole card.
/// </summary>
internal static class PanelLines
{
    private const int OuterPad = 2;

    private const int ChromeColumns = 4;

    internal readonly record struct WrappedSegment(int Start, string Text);

    internal static bool Supports(TranscriptKind kind) =>
        kind is TranscriptKind.User
            or TranscriptKind.Thinking
            or TranscriptKind.Tool
            or TranscriptKind.Error;

    internal static int PanelWidth(int width) => Math.Max(width, WidgetPaint.MinimumWidth);

    internal static int ContentWidth(int panelWidth) =>
        Math.Max(panelWidth - OuterPad - ChromeColumns, 1);

    internal static List<PaintLine>? TryCreate(TranscriptKind kind, string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Supports(kind) || text.Length == 0)
        {
            return null;
        }

        var panelWidth = PanelWidth(width);
        var body = new List<WrappedSegment>();
        Wrap(text, ContentWidth(panelWidth), 0, body);
        var lines = new List<PaintLine>(body.Count + 2);
        WriteChrome(lines, kind, panelWidth, body);
        return lines;
    }

    internal static List<PaintLine> Create(TranscriptKind kind, string text, int width) =>
        TryCreate(kind, text, width) ?? [];

    internal static void Wrap(string text, int contentWidth, int origin, List<WrappedSegment> into)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(into);
        if (contentWidth < 1)
        {
            contentWidth = 1;
        }

        var index = 0;
        while (index < text.Length)
        {
            var paragraphStart = index;
            while (index < text.Length && !IsNewline(text, index))
            {
                index++;
            }

            WrapParagraph(text, paragraphStart, index, contentWidth, origin, into);
            if (index >= text.Length)
            {
                break;
            }

            index = SkipNewline(text, index);
            if (index == text.Length)
            {
                into.Add(new WrappedSegment(origin + index, string.Empty));
            }
        }
    }

    internal static void WriteChrome(
        List<PaintLine> lines,
        TranscriptKind kind,
        int panelWidth,
        IReadOnlyList<WrappedSegment> body)
    {
        var header = TranscriptCard.Header(kind) ?? string.Empty;
        var border = TranscriptCard.BorderColor(kind);
        var color = TranscriptCard.Color(kind);
        var contentWidth = ContentWidth(panelWidth);
        lines.Add(Top(border, header, panelWidth));
        for (var i = 0; i < body.Count; i++)
        {
            lines.Add(Body(border, color, body[i].Text, contentWidth));
        }

        lines.Add(Bottom(border, panelWidth));
    }

    internal static PaintLine Top(string border, string header, int panelWidth)
    {
        var inner = Math.Max(panelWidth - OuterPad, 3);
        var dashes = inner - header.Length - 3;
        if (dashes < 0)
        {
            var keep = Math.Max(inner - 3, 0);
            header = header[..Math.Min(keep, header.Length)];
            dashes = Math.Max(inner - header.Length - 3, 0);
        }

        var rule = "╭─" + header + new string('─', dashes) + "╮";
        return Rule(border, rule);
    }

    internal static PaintLine Bottom(string border, int panelWidth)
    {
        var inner = Math.Max(panelWidth - OuterPad, 2);
        var rule = "╰" + new string('─', inner - 2) + "╯";
        return Rule(border, rule);
    }

    internal static PaintLine Body(string border, string color, string content, int contentWidth)
    {
        var columns = TextWidth.Measure(content);
        if (columns > contentWidth)
        {
            content = TextWidth.Truncate(content, contentWidth);
            columns = TextWidth.Measure(content);
        }

        var gap = contentWidth - columns;
        var plain = new string(' ', OuterPad) + "│ " + content + new string(' ', gap) + " │";
        var markup = new StringBuilder();
        markup.Append(' ', OuterPad);
        markup.Append('[').Append(border).Append("]│[/] ");
        if (content.Length > 0)
        {
            markup.Append('[').Append(color).Append(']')
                .Append(MarkupText.Escape(content))
                .Append("[/]");
        }

        markup.Append(' ', gap + 1);
        markup.Append('[').Append(border).Append("]│[/]");
        return new PaintLine(markup.ToString(), plain);
    }

    private static PaintLine Rule(string border, string rule)
    {
        var plain = new string(' ', OuterPad) + rule;
        var markup = new string(' ', OuterPad) + "[" + border + "]" + rule + "[/]";
        return new PaintLine(markup, plain);
    }

    private static void WrapParagraph(
        string text,
        int start,
        int end,
        int width,
        int origin,
        List<WrappedSegment> into)
    {
        if (start >= end)
        {
            into.Add(new WrappedSegment(origin + start, string.Empty));
            return;
        }

        var lineStart = start;
        while (lineStart < end)
        {
            var fit = Fit(text, lineStart, end, width);
            if (fit.LineEnd <= lineStart)
            {
                fit = new FitResult(TextWidth.MoveRight(text, lineStart), fit.Next);
                if (fit.LineEnd > end)
                {
                    fit = new FitResult(end, end);
                }
            }

            into.Add(new WrappedSegment(origin + lineStart, Display(text, lineStart, fit.LineEnd)));
            if (fit.Next <= lineStart)
            {
                break;
            }

            lineStart = fit.Next;
        }
    }

    private readonly record struct FitResult(int LineEnd, int Next);

    private static FitResult Fit(string text, int start, int end, int width)
    {
        var columns = 0;
        var index = start;
        while (index < end)
        {
            if (text[index] == ' ')
            {
                if (columns + 1 > width)
                {
                    return new FitResult(index, index + 1);
                }

                columns++;
                index++;
                continue;
            }

            var wordEnd = index;
            while (wordEnd < end && text[wordEnd] != ' ')
            {
                wordEnd = TextWidth.MoveRight(text, wordEnd);
            }

            var wordColumns = MeasureRange(text, index, wordEnd);
            if (columns + wordColumns <= width)
            {
                columns += wordColumns;
                index = wordEnd;
                continue;
            }

            if (index > start)
            {
                return new FitResult(index, index);
            }

            var hard = index;
            var hardColumns = 0;
            while (hard < wordEnd)
            {
                var next = TextWidth.MoveRight(text, hard);
                var runeColumns = MeasureRange(text, hard, next);
                if (hardColumns + runeColumns > width && hard > index)
                {
                    break;
                }

                hardColumns += runeColumns;
                hard = next;
            }

            if (hard == index)
            {
                hard = TextWidth.MoveRight(text, index);
            }

            return new FitResult(hard, hard);
        }

        return new FitResult(end, end);
    }

    private static int MeasureRange(string text, int start, int end)
    {
        var columns = 0;
        var index = start;
        while (index < end)
        {
            if (text[index] == '\t')
            {
                columns += TextWidth.TabColumns;
                index++;
                continue;
            }

            var next = TextWidth.MoveRight(text, index);
            if (next > end)
            {
                next = end;
            }

            if (next <= index)
            {
                break;
            }

            columns += TextWidth.Measure(text.AsSpan(index, next - index));
            index = next;
        }

        return columns;
    }

    private static string Display(string text, int start, int end)
    {
        var builder = new StringBuilder(end - start);
        for (var i = start; i < end; i++)
        {
            if (text[i] == '\t')
            {
                builder.Append(' ', TextWidth.TabColumns);
                continue;
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    private static bool IsNewline(string text, int index) =>
        text[index] is '\n' or '\r';

    private static int SkipNewline(string text, int index)
    {
        if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
        {
            return index + 2;
        }

        return index + 1;
    }
}
