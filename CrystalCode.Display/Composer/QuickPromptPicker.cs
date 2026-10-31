using CrystalCode.Display.Input;
using CrystalCode.Display.Input.Interfaces;
using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;

namespace CrystalCode.Display.Composer;

/// <summary>
/// Owns isolated search, selection, action, and preview scrolling. It paints a
/// full-screen frame: the list beside the preview on a wide terminal, stacked
/// on a narrow one, each with its own scrollbar.
/// </summary>
public sealed class QuickPromptPicker(IReadOnlyList<QuickPromptOption> options, string directory, string? error)
{
    private const int SideBySideMinimumWidth = 100;
    private const int MaximumStackedListRows = 8;
    private const int FrameColumns = 4;
    private const int SeparatorColumns = 3;
    private const int MinimumWidth = 20;
    private const int MinimumHeight = 8;
    private readonly ComposerBuffer _search = new();
    private int _selected;
    private int _scroll;
    private string? _notice;
    private string? _matchedQuery;
    private QuickPromptOption[] _matches = [];
    private QuickPromptOption? _wrapped;
    private int _wrappedWidth;
    private IReadOnlyList<string> _preview = [];
    private int _previewRows = 5;

    public bool Send { get; private set; }
    public bool Closed { get; private set; }
    public QuickPromptOption? Chosen { get; private set; }

    public void Handle(IInputEvent item)
    {
        var matches = Matches();
        _notice = null;
        switch (item)
        {
            case InputKey { Key: ConsoleKey.Escape }:
                Closed = true;
                break;
            case InputKey { Key: ConsoleKey.Tab }:
                Send = !Send;
                break;
            case InputKey { Key: ConsoleKey.Enter }:
                Apply(matches);
                break;
            case InputKey { Key: ConsoleKey.UpArrow }:
                _selected = Math.Max(0, _selected - 1);
                _scroll = 0;
                break;
            case InputKey { Key: ConsoleKey.DownArrow }:
                _selected = Math.Min(Math.Max(0, matches.Length - 1), _selected + 1);
                _scroll = 0;
                break;
            case InputKey { Key: ConsoleKey.PageUp }:
                _scroll = Math.Max(0, _scroll - Math.Max(1, _previewRows - 1));
                break;
            case InputKey { Key: ConsoleKey.PageDown }:
                _scroll += Math.Max(1, _previewRows - 1);
                break;
            case InputWheel wheel:
                _scroll = Math.Max(0, _scroll - wheel.Delta);
                break;
            case InputPaste paste:
                _search.Insert(paste.Text.Replace('\n', ' '));
                _selected = 0;
                _scroll = 0;
                break;
            case InputKey key:
                _search.Handle(key);
                _selected = 0;
                _scroll = 0;
                break;
            default:
                break;
        }
    }

    public IReadOnlyList<PaintLine> Paint(int width, int height)
    {
        width = Math.Max(width, MinimumWidth);
        height = Math.Max(height, MinimumHeight);
        var inner = width - FrameColumns;
        var matches = Matches();
        _selected = Math.Clamp(_selected, 0, Math.Max(0, matches.Length - 1));
        var head = new List<PaintLine>
        {
            Muted("Type to search  Up/Down select  Enter apply  Esc cancel", inner),
            Status(matches.Length, inner),
            SearchLine(inner)
        };
        AddNotices(head, inner);
        var bodyRows = Math.Max(2, height - 2 - head.Count);
        var lines = new List<PaintLine>(height) { Top(width) };
        lines.AddRange(head.Select(line => Row(line, inner)));
        lines.AddRange(matches.Length == 0
            ? EmptyBody(inner, bodyRows)
            : Body(matches, inner, bodyRows));
        lines.Add(Bottom(width));
        return lines;
    }

    private void Apply(QuickPromptOption[] matches)
    {
        if (matches.Length == 0)
        {
            return;
        }

        var option = matches[_selected];
        if (option.Error is not null)
        {
            _notice = $"{option.Name} cannot be used: {option.Error}";
            return;
        }

        Chosen = option;
        Closed = true;
    }

    private QuickPromptOption[] Matches()
    {
        var query = _search.Text;
        if (_matchedQuery != query)
        {
            _matches = options.Where(option =>
                option.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || option.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            _matchedQuery = query;
        }

        return _matches;
    }

    private IReadOnlyList<string> Preview(QuickPromptOption option, int width)
    {
        if (!ReferenceEquals(_wrapped, option) || _wrappedWidth != width)
        {
            _preview = TextWidth.Wrap(option.Text, width);
            _wrapped = option;
            _wrappedWidth = width;
        }

        return _preview;
    }

    private List<PaintLine> EmptyBody(int inner, int rows)
    {
        var message = options.Count == 0
            ? "No quick prompts. Save UTF-8 .md files in the directory above."
            : "No matches.";
        var body = new List<PaintLine>(rows) { Row(Muted(message, inner), inner) };
        while (body.Count < rows)
        {
            body.Add(Row(PaintLine.Blank, inner));
        }

        return body;
    }

    private List<PaintLine> Body(QuickPromptOption[] matches, int inner, int rows)
    {
        var sideBySide = inner >= SideBySideMinimumWidth - FrameColumns;
        int listColumns;
        int previewColumns;
        int listRows;
        int previewRows;
        if (sideBySide)
        {
            listColumns = Math.Clamp(inner * 35 / 100, 24, 44);
            previewColumns = inner - listColumns - SeparatorColumns;
            listRows = rows - 1;
            previewRows = rows - 1;
        }
        else
        {
            listColumns = inner;
            previewColumns = inner;
            listRows = Math.Clamp((rows - 1) / 3, 1, MaximumStackedListRows);
            previewRows = Math.Max(1, rows - listRows - 1);
        }

        var listText = Math.Max(1, listColumns - TranscriptScrollbar.Columns);
        var previewText = Math.Max(1, previewColumns - TranscriptScrollbar.Columns);
        var start = Math.Clamp(_selected - listRows + 1, 0, Math.Max(0, matches.Length - listRows));
        var preview = Preview(matches[_selected], previewText);
        _previewRows = previewRows;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, preview.Count - previewRows));

        var listThumb = TranscriptScrollbar.ThumbFromTop(matches.Length, listRows, start);
        var previewThumb = TranscriptScrollbar.ThumbFromTop(preview.Count, previewRows, _scroll);
        var listColumn = new List<PaintLine>(listRows);
        for (var row = 0; row < listRows; row++)
        {
            var index = start + row;
            var content = index < matches.Length
                ? ListRow(matches[index], index == _selected, listText)
                : PaintLine.Blank;
            listColumn.Add(Column(content, listColumns, TranscriptScrollbar.Cell(row, listThumb)));
        }

        var previewColumn = new List<PaintLine>(previewRows);
        for (var row = 0; row < previewRows; row++)
        {
            var index = _scroll + row;
            var content = index < preview.Count
                ? new PaintLine(MarkupText.Escape(preview[index]), preview[index])
                : PaintLine.Blank;
            previewColumn.Add(Column(content, previewColumns, TranscriptScrollbar.Cell(row, previewThumb)));
        }

        var title = PreviewTitle(preview.Count, previewRows);
        var body = new List<PaintLine>(rows);
        if (sideBySide)
        {
            var separator = new PaintLine($" {Border("\u2502")} ", " \u2502 ");
            body.Add(Row(Join(Pad(Muted("Prompts", listColumns), listColumns), separator, Muted(title, previewColumns)), inner));
            for (var row = 0; row < listRows; row++)
            {
                body.Add(Row(Join(listColumn[row], separator, previewColumn[row]), inner));
            }

            return body;
        }

        body.AddRange(listColumn.Select(line => Row(line, inner)));
        body.Add(Row(Muted(title, inner), inner));
        body.AddRange(previewColumn.Select(line => Row(line, inner)));
        return body;
    }

    private PaintLine SearchLine(int inner)
    {
        var text = TextWidth.Truncate(_search.Text, Math.Max(1, inner - 9));
        return new PaintLine($"[{Theme.Accent}]Search > [/]{MarkupText.Escape(text)}", "Search > " + text);
    }

    private void AddNotices(List<PaintLine> rows, int inner)
    {
        if (error is not null)
        {
            rows.Add(PaintLine.Colored(Theme.Warning, TextWidth.Truncate(error, inner)));
        }

        if (_notice is not null)
        {
            rows.Add(PaintLine.Colored(Theme.Warning, TextWidth.Truncate(_notice, inner)));
        }
    }

    private PaintLine Status(int count, int inner)
    {
        var action = Send ? "Fill / [Send]" : "[Fill] / Send";
        var total = count == options.Count ? $"{count} prompts" : $"{count} of {options.Count} prompts";
        var tail = $"(Tab)   {total}   {directory}";
        var plain = $"Action: {action} {tail}";
        if (TextWidth.Measure(plain) > inner)
        {
            return Muted(plain, inner);
        }

        // The chosen action is highlighted; the plain text is unchanged.
        var fill = Send ? Dim("Fill") : Lit("[Fill]");
        var send = Send ? Lit("[Send]") : Dim("Send");
        var markup = Dim("Action: ") + fill + Dim(" / ") + send + Dim(" " + tail);
        return new PaintLine(markup, plain);

        static string Dim(string text) => $"[{Theme.Muted}]{MarkupText.Escape(text)}[/]";
        static string Lit(string text) => $"[{Theme.Accent}]{MarkupText.Escape(text)}[/]";
    }

    private string PreviewTitle(int lines, int available)
    {
        var last = Math.Min(lines, _scroll + available);
        return $"Preview {Math.Min(lines, _scroll + 1)}-{last}/{Math.Max(1, lines)}   PgUp/PgDn or wheel";
    }

    private static PaintLine ListRow(QuickPromptOption option, bool selected, int columns)
    {
        var mark = selected ? "> " : "  ";
        var color = selected ? Theme.Selected : Theme.Chrome;
        if (option.Error is not null)
        {
            var disabled = TextWidth.Truncate($"{mark}{option.Name}  [disabled] {option.Error}", columns);
            return PaintLine.Colored(selected ? Theme.Selected : Theme.Warning, disabled);
        }

        var name = TextWidth.Truncate(mark + option.Name, columns);
        var plain = name;
        var markup = $"[{color}]{MarkupText.Escape(name)}[/]";
        var room = columns - TextWidth.Measure(name) - 2;
        if (room >= 6 && Summary(option.Text) is { Length: > 0 } summary)
        {
            var shown = TextWidth.Truncate(summary, room);
            plain += "  " + shown;
            markup += $"[{Theme.Muted}]{MarkupText.Escape("  " + shown)}[/]";
        }

        return new PaintLine(markup, plain);
    }

    private static string Summary(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim().Replace('\t', ' ');
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static PaintLine Column(PaintLine content, int columns, PaintLine cell)
    {
        var text = Math.Max(1, columns - TranscriptScrollbar.Columns);
        var fitted = content.Fit(text);
        var pad = new string(' ', text - TextWidth.Measure(fitted.Plain) + TranscriptScrollbar.Columns - 1);
        return new PaintLine(fitted.Markup + pad + cell.Markup, fitted.Plain + pad + cell.Plain);
    }

    private static PaintLine Pad(PaintLine line, int columns)
    {
        var fitted = line.Fit(columns);
        var pad = new string(' ', columns - TextWidth.Measure(fitted.Plain));
        return new PaintLine(fitted.Markup + pad, fitted.Plain + pad);
    }

    private static PaintLine Join(PaintLine left, PaintLine separator, PaintLine right) =>
        new(left.Markup + separator.Markup + right.Markup, left.Plain + separator.Plain + right.Plain);

    private static PaintLine Row(PaintLine content, int inner)
    {
        var fitted = content.Fit(inner);
        var pad = new string(' ', inner - TextWidth.Measure(fitted.Plain));
        return new PaintLine(
            Border("\u2502 ") + fitted.Markup + pad + Border(" \u2502"),
            "\u2502 " + fitted.Plain + pad + " \u2502");
    }

    private static PaintLine Top(int width)
    {
        var title = " Quick Prompts ";
        var plain = "\u256d\u2500" + title + new string('\u2500', Math.Max(0, width - 3 - title.Length)) + "\u256e";
        return new PaintLine(Border(plain), plain);
    }

    private static PaintLine Bottom(int width)
    {
        var plain = "\u2570" + new string('\u2500', Math.Max(0, width - 2)) + "\u256f";
        return new PaintLine(Border(plain), plain);
    }

    private static PaintLine Muted(string text, int columns) =>
        PaintLine.Colored(Theme.Muted, TextWidth.Truncate(text, columns));

    private static string Border(string text) =>
        $"[{Theme.Chrome}]{MarkupText.Escape(text)}[/]";
}
