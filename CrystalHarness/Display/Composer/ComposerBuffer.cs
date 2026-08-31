using System.Text;

using CrystalHarness.Display.Paint;

namespace CrystalHarness.Display.Composer;

/// <summary>
/// Multiline prompt buffer. No console writes.
/// </summary>
public sealed class ComposerBuffer
{
    private const int MaximumHistory = 200;
    private readonly StringBuilder _text = new();
    private readonly List<string> _history = [];
    private int _cursor;
    private int _historyIndex;
    private string _draft = string.Empty;

    public bool PlanMode { get; set; }

    public string Text => _text.ToString();

    public int Cursor => _cursor;

    public bool IsEmpty => _text.Length == 0;

    public ComposerAction Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Tab)
        {
            return ComposerAction.TogglePlan;
        }

        if (IsNewline(key))
        {
            Insert("\n");
            return ComposerAction.None;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (TryConsumeBackslashNewline())
            {
                return ComposerAction.None;
            }

            return ComposerAction.Submit;
        }

        if (key.KeyChar == '?' && _text.Length == 0)
        {
            return ComposerAction.ShowHelp;
        }

        var isAlt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);
        var isCtrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);

        switch (key.Key)
        {
            case ConsoleKey.Backspace when ComposerKeys.IsWordDeleteLeft(key):
                DeleteWordLeft();
                break;
            case ConsoleKey.Backspace:
            case ConsoleKey.H when isCtrl && !isAlt:
                DeleteLeft();
                break;
            case ConsoleKey.Delete when isAlt || isCtrl:
                DeleteWordRight();
                break;
            case ConsoleKey.Delete:
                DeleteRight();
                break;
            case ConsoleKey.B when isAlt:
            case ConsoleKey.LeftArrow when isCtrl || isAlt:
                _cursor = WordLeft();
                break;
            case ConsoleKey.F when isAlt:
            case ConsoleKey.RightArrow when isCtrl || isAlt:
                _cursor = WordRight();
                break;
            case ConsoleKey.LeftArrow:
                _cursor = TextWidth.MoveLeft(Text, _cursor);
                break;
            case ConsoleKey.RightArrow:
                _cursor = TextWidth.MoveRight(Text, _cursor);
                break;
            case ConsoleKey.Home:
            case ConsoleKey.A when isCtrl:
                _cursor = LineStart();
                break;
            case ConsoleKey.End:
            case ConsoleKey.E when isCtrl:
                _cursor = LineEnd();
                break;
            case ConsoleKey.UpArrow:
            case ConsoleKey.P when isCtrl:
                RecallHistory(-1);
                break;
            case ConsoleKey.DownArrow:
            case ConsoleKey.N when isCtrl:
                RecallHistory(1);
                break;
            case ConsoleKey.Escape:
                Clear();
                break;
            case ConsoleKey.U when isCtrl:
                DeleteToLineStart();
                break;
            case ConsoleKey.K when isCtrl:
                DeleteToLineEnd();
                break;
            case ConsoleKey.W when isCtrl:
                DeleteWordLeft();
                break;
            case ConsoleKey.D when isAlt:
                DeleteWordRight();
                break;
            default:
                if (key.KeyChar is '\b' or '\u007f')
                {
                    DeleteLeft();
                    break;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    Insert(key.KeyChar.ToString());
                }

                break;
        }

        return ComposerAction.None;
    }

    public void Replace(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text.Clear();
        _text.Append(text);
        _cursor = _text.Length;
    }

    public void Insert(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        _text.Insert(_cursor, text);
        _cursor += text.Length;
    }

    public void Clear()
    {
        _text.Clear();
        _cursor = 0;
        _historyIndex = _history.Count;
        _draft = string.Empty;
    }

    public void RememberAndClear()
    {
        var value = Text;
        if (!string.IsNullOrWhiteSpace(value)
            && (_history.Count == 0
                || !string.Equals(_history[^1], value, StringComparison.Ordinal)))
        {
            _history.Add(value);
            if (_history.Count > MaximumHistory)
            {
                _history.RemoveAt(0);
            }
        }

        Clear();
    }

    public ComposerView Project(int width, int maxRows)
    {
        var mode = ModeLabel.For(PlanMode);
        var promptPlain = mode + " > ";
        var promptColumns = TextWidth.Measure(promptPlain);
        var bodyWidth = Math.Max(width - promptColumns, 8);
        var text = Text;
        var wrapped = TextWidth.Wrap(text, bodyWidth);
        if (wrapped.Count == 0)
        {
            wrapped.Add(string.Empty);
        }

        var (cursorRow, cursorBody) = MapCursor(text, _cursor, bodyWidth);
        var lines = new List<PaintLine>(wrapped.Count);
        var modeColor = PlanMode ? Theme.Plan : Theme.Work;

        for (var i = 0; i < wrapped.Count; i++)
        {
            var body = wrapped[i];
            if (i == 0)
            {
                if (string.IsNullOrEmpty(text))
                {
                    var placeholder = "Ask anything... (Tab: switch mode, /: commands, ?: help)";
                    var availableWidth = Math.Max(width - promptColumns, 0);
                    var truncatedPlaceholder = TextWidth.Truncate(placeholder, availableWidth);
                    var plain = promptPlain + truncatedPlaceholder;
                    var markup = $"[{modeColor} bold]{MarkupText.Escape(mode)}[/]"
                        + $"[{Theme.Chrome}] > [/]"
                        + $"[{Theme.Muted}]{MarkupText.Escape(truncatedPlaceholder)}[/]";
                    lines.Add(new PaintLine(markup, plain));
                }
                else
                {
                    var plain = promptPlain + body;
                    var markup = $"[{modeColor} bold]{MarkupText.Escape(mode)}[/]"
                        + $"[{Theme.Chrome}] > [/]{MarkupText.Escape(body)}";
                    lines.Add(new PaintLine(markup, plain));
                }
            }
            else
            {
                var plain = new string(' ', promptColumns) + body;
                lines.Add(new PaintLine(MarkupText.Escape(plain), plain));
            }
        }

        maxRows = Math.Max(1, maxRows);
        if (lines.Count > maxRows)
        {
            var start = cursorRow < maxRows ? 0 : cursorRow - maxRows + 1;
            start = Math.Clamp(start, 0, lines.Count - maxRows);
            lines = lines.GetRange(start, maxRows);
            cursorRow -= start;
        }

        return new ComposerView(lines, cursorRow, promptColumns + cursorBody);
    }

    private static bool IsNewline(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.J && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return true;
        }

        if (key.Key == ConsoleKey.Enter && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            return true;
        }

        return key.KeyChar == '\n' && key.Key != ConsoleKey.Enter;
    }

    private bool TryConsumeBackslashNewline()
    {
        if (_cursor == 0 || _cursor != _text.Length)
        {
            return false;
        }

        if (_text[_cursor - 1] != '\\')
        {
            return false;
        }

        _text.Remove(_cursor - 1, 1);
        _cursor--;
        Insert("\n");
        return true;
    }

    private void DeleteLeft()
    {
        if (_cursor == 0)
        {
            return;
        }

        var from = TextWidth.MoveLeft(Text, _cursor);
        _text.Remove(from, _cursor - from);
        _cursor = from;
    }

    private void DeleteRight()
    {
        if (_cursor >= _text.Length)
        {
            return;
        }

        var to = TextWidth.MoveRight(Text, _cursor);
        _text.Remove(_cursor, to - _cursor);
    }

    private void DeleteWordRight()
    {
        if (_cursor >= _text.Length)
        {
            return;
        }

        var to = WordRight();
        _text.Remove(_cursor, to - _cursor);
    }

    private void DeleteToLineStart()
    {
        var start = LineStart();
        if (start == _cursor)
        {
            return;
        }

        _text.Remove(start, _cursor - start);
        _cursor = start;
    }

    private void DeleteToLineEnd()
    {
        var end = LineEnd();
        if (end == _cursor)
        {
            return;
        }

        _text.Remove(_cursor, end - _cursor);
    }

    private void DeleteWordLeft()
    {
        var from = WordLeft();
        if (from == _cursor)
        {
            return;
        }

        _text.Remove(from, _cursor - from);
        _cursor = from;
    }

    private int LineStart()
    {
        var text = Text;
        var index = _cursor;
        while (index > 0 && text[index - 1] != '\n')
        {
            index--;
        }

        return index;
    }

    private int LineEnd()
    {
        var text = Text;
        var index = _cursor;
        while (index < text.Length && text[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private int WordLeft()
    {
        var text = Text;
        var index = _cursor;
        while (index > 0 && char.IsWhiteSpace(text[index - 1]))
        {
            index--;
        }

        while (index > 0 && !char.IsWhiteSpace(text[index - 1]))
        {
            index--;
        }

        return index;
    }

    private int WordRight()
    {
        var text = Text;
        var index = _cursor;
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private void RecallHistory(int delta)
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex == _history.Count)
        {
            _draft = Text;
        }

        var next = _historyIndex + delta;
        if (next < 0 || next > _history.Count)
        {
            return;
        }

        _historyIndex = next;
        _text.Clear();
        _text.Append(_historyIndex == _history.Count ? _draft : _history[_historyIndex]);
        _cursor = _text.Length;
    }

    private static (int Row, int Column) MapCursor(string text, int cursor, int bodyWidth)
    {
        var row = 0;
        var column = 0;
        var index = 0;
        while (index < cursor && index < text.Length)
        {
            if (text[index] == '\n')
            {
                row++;
                column = 0;
                index++;
                continue;
            }

            var next = TextWidth.MoveRight(text, index);
            var width = TextWidth.Measure(text.AsSpan(index, next - index));
            if (column + width > bodyWidth && column > 0)
            {
                row++;
                column = 0;
            }

            column += width;
            index = next;
        }

        return (row, column);
    }
}
