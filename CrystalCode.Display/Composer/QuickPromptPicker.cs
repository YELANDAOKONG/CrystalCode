using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace CrystalCode.Display.Composer;

/// <summary>Owns isolated search, selection, action, and preview scrolling.</summary>
public sealed class QuickPromptPicker(IReadOnlyList<QuickPromptOption> options, string directory, string? error)
{
    private const int MaximumListRows = 5;
    private const int MinimumPreviewRows = 3;
    private const int FrameRows = 2;
    private const int FrameColumns = 4;
    private readonly ComposerBuffer _search = new();
    private int _selected;
    private int _scroll;
    private string? _notice;
    private string? _matchedQuery;
    private QuickPromptOption[] _matches = [];
    private QuickPromptOption? _wrapped;
    private int _wrappedWidth;
    private IReadOnlyList<string> _preview = [];

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
                _scroll = Math.Max(0, _scroll - MaximumListRows);
                break;
            case InputKey { Key: ConsoleKey.PageDown }:
                _scroll += MaximumListRows;
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

    public IReadOnlyList<PaintLine> Paint(int width, int height) =>
        WidgetPaint.Lines(Render(width, height), width);

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

    private IRenderable Render(int width, int height)
    {
        var inner = Math.Max(8, width - FrameColumns);
        var matches = Matches();
        _selected = Math.Clamp(_selected, 0, Math.Max(0, matches.Length - 1));
        var rows = new List<IRenderable>
        {
            Muted("Type to search  Up/Down select  Enter apply  Esc cancel", inner),
            Muted(StatusText(matches.Length), inner),
            new Markup($"[{Theme.Accent}]Search > [/]{MarkupText.Escape(TextWidth.Truncate(_search.Text, Math.Max(1, inner - 9)))}")
        };
        AddNotices(rows, inner);
        if (matches.Length == 0)
        {
            rows.Add(Muted(options.Count == 0
                ? "No quick prompts. Save UTF-8 .md files in the directory above."
                : "No matches.", inner));
            return Frame(rows);
        }

        var reserved = FrameRows + rows.Count + 1 + MinimumPreviewRows;
        var visible = Math.Clamp(height - reserved, 1, MaximumListRows);
        var start = Math.Clamp(_selected - visible + 1, 0, Math.Max(0, matches.Length - visible));
        var end = Math.Min(matches.Length, start + visible);
        for (var index = start; index < end; index++)
        {
            rows.Add(ListRow(matches[index], index == _selected, inner));
        }

        var preview = Preview(matches[_selected], inner);
        var available = Math.Max(1, height - FrameRows - rows.Count - 1);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, preview.Count - available));
        rows.Add(new Rule($"[{Theme.Muted}]{MarkupText.Escape(PreviewTitle(preview.Count, available))}[/]")
        {
            Justification = Justify.Left,
            Style = Style.Parse(Theme.Muted)
        });
        foreach (var line in preview.Skip(_scroll).Take(available))
        {
            rows.Add(new Markup(MarkupText.Escape(line)));
        }

        return Frame(rows);
    }

    private void AddNotices(List<IRenderable> rows, int inner)
    {
        if (error is not null)
        {
            rows.Add(new Markup($"[{Theme.Warning}]{MarkupText.Escape(TextWidth.Truncate(error, inner))}[/]"));
        }

        if (_notice is not null)
        {
            rows.Add(new Markup($"[{Theme.Warning}]{MarkupText.Escape(TextWidth.Truncate(_notice, inner))}[/]"));
        }
    }

    private string StatusText(int count)
    {
        var action = Send ? "Fill / [Send]" : "[Fill] / Send";
        var total = count == options.Count ? $"{count} prompts" : $"{count} of {options.Count} prompts";
        return $"Action: {action} (Tab)   {total}   {directory}";
    }

    private string PreviewTitle(int lines, int available)
    {
        var last = Math.Min(lines, _scroll + available);
        return $"Preview {Math.Min(lines, _scroll + 1)}-{last}/{Math.Max(1, lines)}   PgUp/PgDn or wheel";
    }

    private static IRenderable ListRow(QuickPromptOption option, bool selected, int inner)
    {
        var label = option.Error is null ? option.Name : $"{option.Name}  [disabled] {option.Error}";
        var text = MarkupText.Escape(TextWidth.Truncate((selected ? "> " : "  ") + label, inner));
        return new Markup($"[{(selected ? Theme.Selected : Theme.Chrome)}]{text}[/]");
    }

    private static Markup Muted(string text, int inner) =>
        new($"[{Theme.Muted}]{MarkupText.Escape(TextWidth.Truncate(text, inner))}[/]");

    private static IRenderable Frame(List<IRenderable> rows) => new Panel(new Rows(rows))
    {
        Header = new PanelHeader("Quick Prompts"),
        Border = BoxBorder.Rounded,
        BorderStyle = Style.Parse(Theme.Chrome),
        Padding = new Padding(1, 0, 1, 0),
        Expand = true
    };
}
