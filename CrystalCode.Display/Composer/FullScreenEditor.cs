using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Composer;

/// <summary>
/// Full-screen editor for a long prompt. It owns an isolated buffer, so the
/// composer draft changes only when the editor is applied.
/// </summary>
public sealed class FullScreenEditor
{
    // Left and right border plus one space of padding inside each.
    private const int SideColumns = 2;
    private const int BorderColumns = SideColumns * 2;

    private readonly ComposerBuffer _buffer = new()
    {
        ShowPrompt = false,
        Placeholder = "Type or paste your prompt here."
    };

    public FullScreenEditor(string submitted, bool planMode, string? customMode)
    {
        ArgumentNullException.ThrowIfNull(submitted);
        _buffer.PlanMode = planMode;
        _buffer.CustomMode = customMode;
        _buffer.ReplaceSubmitted(submitted);
    }

    public bool Closed { get; private set; }

    /// <summary>True when the editor closed through apply rather than cancel.</summary>
    public bool Applied { get; private set; }

    /// <summary>Edited text with trusted image marker prefixes, as the composer submits it.</summary>
    public string SubmissionText => _buffer.SubmissionText;

    public void Handle(IInputEvent item)
    {
        switch (item)
        {
            case InputKey { Key: ConsoleKey.Escape }:
                Closed = true;
                break;
            case InputKey { Key: ConsoleKey.F8, Modifiers: ConsoleModifiers.None }:
            case InputKey { Key: ConsoleKey.S, Modifiers: ConsoleModifiers.Control }:
                Applied = true;
                Closed = true;
                break;
            case InputPaste paste:
                _buffer.Insert(paste.Text);
                break;
            case InputKey key:
                HandleKey(key);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Paints a bordered frame. Every row is built to exactly <paramref name="width"/>
    /// columns with the side borders at fixed columns, so scrolling the text
    /// window never moves or breaks the border.
    /// </summary>
    public ComposerView Paint(int width, int height)
    {
        var inner = Math.Max(1, width - BorderColumns);
        var bodyRows = Math.Max(1, height - 2);
        // Project reserves one column for the cursor at the right edge.
        var body = _buffer.Project(inner + 1, bodyRows);
        var lines = new List<PaintLine>(height)
        {
            TopBorder(width)
        };
        for (var row = 0; row < bodyRows; row++)
        {
            lines.Add(row < body.Lines.Count
                ? Side(body.Lines[row], inner)
                : Side(PaintLine.Blank, inner));
        }

        lines.Add(BottomBorder(width));
        return new ComposerView(lines, body.CursorRow + 1, body.CursorColumn + SideColumns);
    }

    private static PaintLine Side(PaintLine content, int inner)
    {
        var fitted = content.Fit(inner);
        var padding = new string(' ', Math.Max(0, inner - TextWidth.Measure(fitted.Plain)));
        var markup = Border("\u2502 ") + fitted.Markup + padding + Border(" \u2502");
        return new PaintLine(markup, "\u2502 " + fitted.Plain + padding + " \u2502");
    }

    private PaintLine TopBorder(int width)
    {
        var title = TextWidth.Truncate(Title(), Math.Max(0, width - 6));
        return Rule("\u256d", "\u256e", title, width);
    }

    private static PaintLine BottomBorder(int width)
    {
        var hints = TextWidth.Truncate("F8 or Ctrl+S: apply   Esc: cancel", Math.Max(0, width - 6));
        return Rule("\u2570", "\u256f", hints, width);
    }

    private static PaintLine Rule(string left, string right, string label, int width)
    {
        var text = label.Length == 0 ? string.Empty : " " + label + " ";
        var dashes = Math.Max(0, width - 2 - 1 - TextWidth.Measure(text));
        var plain = left + "\u2500" + text + new string('\u2500', dashes) + right;
        return new PaintLine(Border(plain), plain);
    }

    private static string Border(string text) =>
        $"[{Theme.Chrome}]{MarkupText.Escape(text)}[/]";

    private void HandleKey(InputKey key)
    {
        // Enter, Tab, and ? mean submit, mode cycle, and help in the composer.
        // Here they are text.
        if (key.Key == ConsoleKey.Enter || key.KeyChar == '\r')
        {
            _buffer.Insert("\n");
            return;
        }

        if (key.Key == ConsoleKey.Tab || key.KeyChar == '\t')
        {
            if (!key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                _buffer.Insert("\t");
            }

            return;
        }

        if (key.KeyChar == '?')
        {
            _buffer.Insert("?");
            return;
        }

        _buffer.Handle(key);
    }

    private string ModeName() =>
        !string.IsNullOrEmpty(_buffer.CustomMode) ? _buffer.CustomMode
        : _buffer.PlanMode ? "Plan"
        : "Work";

    private string Title()
    {
        var text = _buffer.Text;
        var line = 1;
        var total = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            total++;
            if (index < _buffer.Cursor)
            {
                line++;
            }
        }

        return $"{ModeName()}   Line {line}/{total}";
    }
}
