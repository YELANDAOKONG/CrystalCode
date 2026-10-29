using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Composer;

/// <summary>
/// Full-screen editor for a long prompt. It owns an isolated buffer, so the
/// composer draft changes only when the editor is applied.
/// </summary>
public sealed class FullScreenEditor
{
    private readonly ComposerBuffer _buffer = new()
    {
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

    public ComposerView Paint(int width, int height)
    {
        var body = _buffer.Project(width, Math.Max(1, height - 1));
        var lines = new List<PaintLine>(height)
        {
            PaintLine.Colored(Theme.Chrome, Header()).Fit(width)
        };
        lines.AddRange(body.Lines);
        while (lines.Count < height)
        {
            lines.Add(PaintLine.Blank);
        }

        return new ComposerView(lines, body.CursorRow + 1, body.CursorColumn);
    }

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

    private string Header()
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

        return $"Line {line}/{total}   F8 or Ctrl+S: apply   Esc: cancel";
    }
}
