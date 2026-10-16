using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Composer;

/// <summary>Owns isolated search, selection, action, and preview scrolling.</summary>
public sealed class QuickPromptPicker(IReadOnlyList<QuickPromptOption> options, string directory, string? error)
{
    private const int VisibleOptions = 5;
    private readonly ComposerBuffer _search = new();
    private int _selected;
    private int _scroll;
    public bool Send { get; private set; }
    public bool Closed { get; private set; }
    public QuickPromptOption? Chosen { get; private set; }

    private QuickPromptOption[] Matches() => options.Where(option =>
        option.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)
        || option.Text.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();

    public void Handle(IInputEvent item)
    {
        var matches = Matches();
        switch (item)
        {
            case InputKey { Key: ConsoleKey.Escape }:
                Closed = true;
                break;
            case InputKey { Key: ConsoleKey.Tab }:
                Send = !Send;
                break;
            case InputKey { Key: ConsoleKey.Enter }:
                if (matches.Length > 0 && matches[_selected].Error is null)
                {
                    Chosen = matches[_selected];
                    Closed = true;
                }
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
                _scroll = Math.Max(0, _scroll - VisibleOptions);
                break;
            case InputKey { Key: ConsoleKey.PageDown }:
                _scroll += VisibleOptions;
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
        var lines = new List<PaintLine>();
        void Add(string text, string? color = null) =>
            lines.Add(PaintLine.Colored(color ?? Theme.Chrome, text).Fit(width));
        Add($"Quick prompts   {(Send ? "Fill / [Send]" : "[Fill] / Send")}   Tab: action   Enter: apply   Esc: cancel");
        Add(directory, Theme.Muted);
        Add("Search: " + _search.Text);
        if (error is not null)
        {
            Add(error);
        }
        var matches = Matches();
        if (matches.Length == 0)
        {
            Add(options.Count == 0 ? "No quick prompts. Save UTF-8 .md files in the directory above." : "No matches.");
            return lines;
        }
        var start = Math.Max(0, _selected - VisibleOptions + 1);
        foreach (var index in Enumerable.Range(start, Math.Min(VisibleOptions, matches.Length - start)))
        {
            var option = matches[index];
            Add($"{(index == _selected ? ">" : " ")} {option.Name}{(option.Error is null ? "" : "  [disabled] " + option.Error)}",
                index == _selected ? Theme.Selected : Theme.Chrome);
        }
        var preview = TextWidth.Wrap(matches[_selected].Text, Math.Max(1, width));
        var available = Math.Max(1, height - lines.Count - 1);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, preview.Count - available));
        Add($"Preview   PgUp/PgDn or wheel   {_scroll + 1}/{Math.Max(1, preview.Count)}", Theme.Muted);
        foreach (var line in preview.Skip(_scroll).Take(available))
        {
            Add(line);
        }
        return lines;
    }
}
