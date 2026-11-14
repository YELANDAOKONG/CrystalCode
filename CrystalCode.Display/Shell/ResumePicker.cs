using CrystalCode.Display.Input;
using CrystalCode.Display.Input.Interfaces;
using CrystalCode.Display.Paint;

namespace CrystalCode.Display.Shell;

/// <summary>Owns search, selection, and scrolling for the full-screen session list.</summary>
public sealed class ResumePicker(
    IReadOnlyList<ResumeOption> options,
    string? currentId,
    bool listWorkspace)
{
    private const int HeaderRows = 4;
    private const int EntryRows = 2;
    private const int FooterRows = 1;
    private const string Indent = "  ";
    private readonly IReadOnlyList<ResumeOption> _options = options;
    private IReadOnlyList<ResumeOption> _matches = options;
    private string _query = string.Empty;
    private int _selected;
    private int _first;
    private int _visible = 1;

    public bool Closed { get; private set; }
    public string? ChosenId { get; private set; }

    public void Handle(IInputEvent input)
    {
        switch (input)
        {
            case InputKey { Key: ConsoleKey.Escape }:
                Closed = true;
                break;
            case InputKey { Key: ConsoleKey.Enter } when _matches.Count > 0:
                ChosenId = _matches[_selected].Id;
                Closed = true;
                break;
            case InputKey { Key: ConsoleKey.UpArrow }:
                MoveSelection(-1);
                break;
            case InputKey { Key: ConsoleKey.DownArrow }:
                MoveSelection(1);
                break;
            case InputKey { Key: ConsoleKey.PageUp }:
                MoveSelection(-_visible);
                break;
            case InputKey { Key: ConsoleKey.PageDown }:
                MoveSelection(_visible);
                break;
            case InputWheel wheel:
                Scroll(-wheel.Delta);
                break;
            case InputKey { Key: ConsoleKey.Backspace } when _query.Length > 0:
                Search(_query[..TextWidth.MoveLeft(_query, _query.Length)]);
                break;
            case InputPaste paste:
                Search(_query + TerminalText.SanitizeLine(paste.Text));
                break;
            case InputKey key when !char.IsControl(key.KeyChar):
                Search(_query + key.KeyChar);
                break;
            default:
                break;
        }
    }

    public IReadOnlyList<PaintLine> Paint(int width, int height)
    {
        var frame = new PaintLine[height];
        Array.Fill(frame, PaintLine.Blank);
        var listRows = height - HeaderRows - FooterRows;
        _visible = Math.Max(1, listRows / EntryRows);
        RevealSelection();

        Write(frame, 0, Theme.Accent, "Resume Session", width);
        Write(frame, 1, Theme.Muted, "Newest first", width);
        Write(frame, 2, Theme.Accent, "Search > " + TerminalText.SanitizeLine(_query), width);
        Write(frame, height - 1, Theme.Muted, "Up/Down select  PgUp/PgDn or wheel scroll  Enter resume  Esc cancel", width);

        if (_matches.Count == 0)
        {
            if (listRows > 0)
            {
                Write(frame, HeaderRows, Theme.Muted, "No matching sessions", width);
            }

            return frame;
        }

        var entries = Math.Min(Math.Max(0, listRows / EntryRows), _matches.Count - _first);
        var rows = new PaintLine[entries * EntryRows];
        var barred = _matches.Count > _visible;
        var bodyWidth = barred ? Math.Max(1, width - TranscriptScrollbar.Columns) : width;
        for (var index = 0; index < entries; index++)
        {
            var session = _matches[_first + index];
            var selected = _first + index == _selected;
            var label = EntryLabel(session, selected, bodyWidth);
            var time = session.UpdatedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "Unknown time";
            var current = string.Equals(session.Id, currentId, StringComparison.Ordinal) ? "  current" : string.Empty;
            Write(rows, index * EntryRows, selected ? Theme.Selected : Theme.User, label, bodyWidth);
            Write(rows, index * EntryRows + 1, Theme.Muted,
                $"{Indent}{session.Id}{Indent}{time}{Indent}{session.UserTurns} turns{current}", bodyWidth);
        }

        IReadOnlyList<PaintLine> body = rows;
        if (barred)
        {
            var rowsBelow = (_matches.Count - _first - entries) * EntryRows;
            body = TranscriptScrollbar.Apply(rows, width, _matches.Count * EntryRows, rowsBelow);
        }

        for (var row = 0; row < body.Count; row++)
        {
            frame[HeaderRows + row] = body[row];
        }

        return frame;
    }

    private void Search(string query)
    {
        _query = query;
        _matches = query.Length == 0
            ? _options
            : _options.Where(session => Matches(session, query)).ToArray();
        _selected = 0;
        _first = 0;
    }

    private static bool Matches(ResumeOption session, string query) =>
        session.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || session.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || session.Preview.Contains(query, StringComparison.OrdinalIgnoreCase)
        || session.Workspace.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(session.Workspace)
            && PathDisplay.Shorten(session.Workspace).Contains(query, StringComparison.OrdinalIgnoreCase));

    private void MoveSelection(int delta)
    {
        _selected = Math.Clamp(_selected + delta, 0, Math.Max(0, _matches.Count - 1));
        RevealSelection();
    }

    private void RevealSelection()
    {
        _first = Math.Clamp(_first, 0, Math.Max(0, _matches.Count - _visible));
        _first = Math.Clamp(_first, Math.Max(0, _selected - _visible + 1), _selected);
    }

    private void Scroll(int delta)
    {
        _first = Math.Clamp(_first + delta, 0, Math.Max(0, _matches.Count - _visible));
        _selected = Math.Clamp(_selected, _first, _first + _visible - 1);
    }

    private string EntryLabel(ResumeOption session, bool selected, int width)
    {
        var marker = selected ? "> " : Indent;
        var title = TerminalText.SanitizeLine(session.Title);
        if (!listWorkspace || string.IsNullOrWhiteSpace(session.Workspace))
        {
            return marker + title;
        }

        var path = TerminalText.SanitizeLine(PathDisplay.Shorten(session.Workspace));
        path = TextWidth.Truncate(path, width / 3);
        var titleWidth = width - TextWidth.Measure(marker + Indent + path);
        return marker + TextWidth.Truncate(title, titleWidth) + Indent + path;
    }

    private static void Write(PaintLine[] frame, int row, string color, string text, int width)
    {
        if (row >= 0 && row < frame.Length)
        {
            frame[row] = PaintLine.Colored(color, TextWidth.Truncate(TerminalText.SanitizeLine(text), width));
        }
    }
}
