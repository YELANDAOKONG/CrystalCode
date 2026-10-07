using System.Text;
using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Composer;

/// <summary>
/// Multiline prompt buffer. No console writes.
/// </summary>
public sealed class ComposerBuffer
{
    private const int MaximumHistory = 200;
    private readonly StringBuilder _text = new();
    private readonly List<string> _history = [];
    private readonly List<AtomicSpan> _atomicSpans = [];
    private readonly List<AtomicSpan> _draftAtomicSpans = [];
    private int _cursor;
    private int _historyIndex;
    private string _draft = string.Empty;
    private int _draftCursor;
    private int _bodyWidth = 80;

    public bool PlanMode { get; set; }

    public string Text => _text.ToString();

    public const char ImageMarkerPrefix = '\u2063';

    public string SubmissionText
    {
        get
        {
            var value = new StringBuilder(Text);
            for (var index = _atomicSpans.Count - 1; index >= 0; index--)
            {
                value.Insert(_atomicSpans[index].Start, ImageMarkerPrefix);
            }

            return value.ToString();
        }
    }

    public int Cursor => _cursor;

    public bool IsEmpty => _text.Length == 0;

    public ComposerAction Handle(ConsoleKeyInfo key) =>
        Handle(InputKey.From(key));

    public ComposerAction Handle(InputKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Key == ConsoleKey.Tab || key.KeyChar == '\t')
        {
            return ComposerAction.TogglePlan;
        }

        if (IsNewline(key))
        {
            Insert("\n");
            return ComposerAction.None;
        }

        if (key.Key == ConsoleKey.Enter || key.KeyChar == '\r')
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

        if (key.Key == ConsoleKey.V && isCtrl && !isAlt)
        {
            return ComposerAction.PasteImage;
        }

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
                _cursor = SnapCursor(WordLeft(), moveRight: false);
                break;
            case ConsoleKey.F when isAlt:
            case ConsoleKey.RightArrow when isCtrl || isAlt:
                _cursor = SnapCursor(WordRight(), moveRight: true);
                break;
            case ConsoleKey.LeftArrow:
                _cursor = SnapCursor(TextWidth.MoveLeft(Text, _cursor), moveRight: false);
                break;
            case ConsoleKey.RightArrow:
                _cursor = SnapCursor(TextWidth.MoveRight(Text, _cursor), moveRight: true);
                break;
            case ConsoleKey.Home:
            case ConsoleKey.A when isCtrl:
                _cursor = SnapCursor(LineStart(), moveRight: false);
                break;
            case ConsoleKey.End:
            case ConsoleKey.E when isCtrl:
                _cursor = SnapCursor(LineEnd(), moveRight: true);
                break;
            case ConsoleKey.UpArrow:
                MoveVerticalOrRecall(-1);
                break;
            case ConsoleKey.P when isCtrl:
                RecallHistory(-1);
                break;
            case ConsoleKey.DownArrow:
                MoveVerticalOrRecall(1);
                break;
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
        DetachHistoryNavigation();
        _text.Clear();
        _text.Append(text);
        _cursor = _text.Length;
        _atomicSpans.Clear();
    }

    public void Insert(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return;
        }

        DetachHistoryNavigation();
        _cursor = SnapCursor(_cursor, moveRight: true);
        for (var index = 0; index < _atomicSpans.Count; index++)
        {
            var span = _atomicSpans[index];
            if (span.Start >= _cursor)
            {
                _atomicSpans[index] = span with { Start = span.Start + text.Length };
            }
        }

        _text.Insert(_cursor, text);
        _cursor += text.Length;
    }

    public void InsertAtomic(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Insert(text);
        _atomicSpans.Add(new AtomicSpan(_cursor - text.Length, text.Length));
        _atomicSpans.Sort(static (left, right) => left.Start.CompareTo(right.Start));
    }

    public void Clear()
    {
        _text.Clear();
        _atomicSpans.Clear();
        _cursor = 0;
        _historyIndex = _history.Count;
        _draft = string.Empty;
        _draftCursor = 0;
        _draftAtomicSpans.Clear();
    }

    public void SeedHistory(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)
                || (_history.Count > 0
                    && string.Equals(_history[^1], entry, StringComparison.Ordinal)))
            {
                continue;
            }

            _history.Add(entry);
            if (_history.Count > MaximumHistory)
            {
                _history.RemoveAt(0);
            }
        }

        _historyIndex = _history.Count;
    }

    public void ForgetImageHistory()
    {
        _history.RemoveAll(entry => entry.Contains(ImageMarkerPrefix));
        _atomicSpans.Clear();
        _draftAtomicSpans.Clear();
        _historyIndex = _history.Count;
        _draft = Text;
        _draftCursor = _cursor;
    }

    public void RememberAndClear()
    {
        var value = Text;
        if (_atomicSpans.Count == 0
            && !string.IsNullOrWhiteSpace(value)
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
        var mode = PlanMode ? "Plan" : "Work";
        var promptPlain = mode + " > ";
        var promptColumns = TextWidth.Measure(promptPlain);
        var bodyWidth = Math.Max(width - promptColumns - 1, 8);
        _bodyWidth = bodyWidth;
        var text = Text;
        var wrapped = TextWidth.Wrap(text, bodyWidth);
        if (wrapped.Count == 0)
        {
            wrapped.Add(string.Empty);
        }

        var (cursorRow, cursorBody) = MapCursor(text, _cursor, bodyWidth);
        var lines = new List<PaintLine>(wrapped.Count);
        var modeColor = PlanMode ? Theme.Plan : Theme.Work;

        var bodyStart = 0;
        for (var i = 0; i < wrapped.Count; i++)
        {
            var body = wrapped[i];
            var bodyMarkup = ColorBody(body, bodyStart);
            var displayBody = TextWidth.ExpandTabs(body);
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
                    var plain = promptPlain + displayBody;
                    var markup = $"[{modeColor} bold]{MarkupText.Escape(mode)}[/]"
                        + $"[{Theme.Chrome}] > [/]{bodyMarkup}";
                    lines.Add(new PaintLine(markup, plain));
                }
            }
            else
            {
                var plain = new string(' ', promptColumns) + displayBody;
                lines.Add(new PaintLine(MarkupText.Escape(new string(' ', promptColumns))
                    + bodyMarkup, plain));
            }

            bodyStart += body.Length;
            if (bodyStart < text.Length && text[bodyStart] == '\n')
            {
                bodyStart++;
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

    private static bool IsNewline(InputKey key)
    {
        // Unix ReadKey reports Ctrl+J as Enter with LF. It still inserts a newline.
        if (key.Modifiers.HasFlag(ConsoleModifiers.Control)
            && (key.Key == ConsoleKey.J || key.KeyChar == '\n'))
        {
            return true;
        }

        if ((key.Key == ConsoleKey.Enter || key.KeyChar == '\r')
            && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
        {
            return true;
        }

        return key.KeyChar == '\n' && key.Key != ConsoleKey.Enter;
    }

    private bool TryConsumeBackslashNewline()
    {
        if (_cursor == 0 || _text[_cursor - 1] != '\\')
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
        DeleteRange(from, _cursor);
    }

    private void DeleteRight()
    {
        if (_cursor >= _text.Length)
        {
            return;
        }

        var to = TextWidth.MoveRight(Text, _cursor);
        DeleteRange(_cursor, to);
    }

    private void DeleteWordRight()
    {
        if (_cursor >= _text.Length)
        {
            return;
        }

        var to = WordRight();
        DeleteRange(_cursor, to);
    }

    private void DeleteToLineStart()
    {
        var start = LineStart();
        if (start == _cursor)
        {
            return;
        }

        DeleteRange(start, _cursor);
    }

    private void DeleteToLineEnd()
    {
        var end = LineEnd();
        if (end == _cursor)
        {
            return;
        }

        DeleteRange(_cursor, end);
    }

    private void DeleteWordLeft()
    {
        var from = WordLeft();
        if (from == _cursor)
        {
            return;
        }

        DeleteRange(from, _cursor);
    }

    private void DeleteRange(int from, int to)
    {
        DetachHistoryNavigation();
        bool expanded;
        do
        {
            expanded = false;
            foreach (var span in _atomicSpans)
            {
                var end = span.Start + span.Length;
                if (from < end && to > span.Start
                    && (from > span.Start || to < end))
                {
                    from = Math.Min(from, span.Start);
                    to = Math.Max(to, end);
                    expanded = true;
                }
            }
        }
        while (expanded);

        _text.Remove(from, to - from);
        _atomicSpans.RemoveAll(span => span.Start < to && span.Start + span.Length > from);
        for (var index = 0; index < _atomicSpans.Count; index++)
        {
            var span = _atomicSpans[index];
            if (span.Start >= to)
            {
                _atomicSpans[index] = span with { Start = span.Start - (to - from) };
            }
        }

        _cursor = from;
    }

    private int SnapCursor(int position, bool moveRight)
    {
        foreach (var span in _atomicSpans)
        {
            var end = span.Start + span.Length;
            if (position > span.Start && position < end)
            {
                return moveRight ? end : span.Start;
            }
        }

        return position;
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
            _draftCursor = _cursor;
            _draftAtomicSpans.Clear();
            _draftAtomicSpans.AddRange(_atomicSpans);
        }

        var next = _historyIndex + delta;
        if (next < 0 || next > _history.Count)
        {
            return;
        }

        _historyIndex = next;
        _text.Clear();
        _text.Append(_historyIndex == _history.Count ? _draft : _history[_historyIndex]);
        _atomicSpans.Clear();
        if (_historyIndex == _history.Count)
        {
            _atomicSpans.AddRange(_draftAtomicSpans);
        }
        _cursor = _historyIndex == _history.Count
            ? _draftCursor
            : delta < 0 ? 0 : _text.Length;
    }

    private void MoveVerticalOrRecall(int delta)
    {
        var text = Text;
        var (row, column) = MapCursor(text, _cursor, _bodyWidth);
        var lastRow = MapCursor(text, text.Length, _bodyWidth).Row;
        var targetRow = row + delta;
        if (targetRow < 0 || targetRow > lastRow)
        {
            RecallHistory(delta);
            return;
        }

        var candidate = _cursor;
        var distance = int.MaxValue;
        var offset = 0;
        var visualRow = 0;
        var visualColumn = 0;
        while (true)
        {
            if (visualRow == targetRow)
            {
                var nextDistance = Math.Abs(visualColumn - column);
                if (nextDistance < distance)
                {
                    candidate = offset;
                    distance = nextDistance;
                }
            }

            if (offset == text.Length)
            {
                break;
            }

            if (text[offset] == '\n')
            {
                visualRow++;
                visualColumn = 0;
                offset++;
                continue;
            }

            var next = TextWidth.MoveRight(text, offset);
            var width = TextWidth.Measure(text.AsSpan(offset, next - offset));
            if (visualColumn + width > _bodyWidth && visualColumn > 0)
            {
                visualRow++;
                visualColumn = 0;
            }

            visualColumn += width;
            offset = next;
        }

        _cursor = SnapCursor(candidate, moveRight: delta > 0);
    }

    private void DetachHistoryNavigation()
    {
        if (_historyIndex == _history.Count)
        {
            return;
        }

        _historyIndex = _history.Count;
        _draft = Text;
        _draftCursor = _cursor;
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

    private string ColorBody(string body, int start)
    {
        var markup = new StringBuilder();
        var cursor = 0;
        foreach (var span in _atomicSpans)
        {
            var from = Math.Max(span.Start - start, 0);
            var to = Math.Min(span.Start + span.Length - start, body.Length);
            if (from >= to)
            {
                continue;
            }

            markup.Append(MarkupText.Escape(TextWidth.ExpandTabs(body[cursor..from])));
            markup.Append($"[{Theme.Image}]");
            markup.Append(MarkupText.Escape(TextWidth.ExpandTabs(body[from..to])));
            markup.Append("[/]");
            cursor = to;
        }

        markup.Append(MarkupText.Escape(TextWidth.ExpandTabs(body[cursor..])));
        return markup.ToString();
    }

    private sealed record AtomicSpan(int Start, int Length);
}
