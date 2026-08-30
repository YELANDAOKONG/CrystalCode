using System.Text;

using Spectre.Console;

namespace CrystalHarness.Display;

/// <summary>
/// Custom prompt editor. Not AnsiConsole.Live.
/// Quiet mode prefix like Claude Code / Cursor CLI, not the Demo diamond prompt.
/// </summary>
public sealed class LineEditor
{
    private const int PollMilliseconds = 40;
    private const int MaximumHistory = 200;
    private readonly List<string> _history = [];

    public async Task<string> ReadAsync(
        bool planMode,
        Func<bool> togglePlan,
        CancellationToken cancellationToken)
    {
        var buffer = new StringBuilder();
        var cursor = 0;
        var mode = planMode;
        var historyIndex = _history.Count;
        var draft = string.Empty;
        WritePrompt(mode);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (!Console.KeyAvailable)
            {
                await Task.Delay(PollMilliseconds, cancellationToken);
            }

            var burst = ReadAvailableKeys();
            if (burst.Count > 1)
            {
                InsertChars(buffer, ref cursor, burst);
                Redraw(mode, buffer, cursor);
                continue;
            }

            var key = burst[0];
            switch (key.Key)
            {
                case ConsoleKey.Tab:
                    mode = togglePlan();
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    Remember(buffer.ToString());
                    return buffer.ToString();
                case ConsoleKey.Backspace:
                    if (cursor == 0)
                    {
                        break;
                    }

                    var removeFrom = TextWidth.MoveLeft(buffer.ToString(), cursor);
                    buffer.Remove(removeFrom, cursor - removeFrom);
                    cursor = removeFrom;
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.Delete:
                    if (cursor >= buffer.Length)
                    {
                        break;
                    }

                    var removeTo = TextWidth.MoveRight(buffer.ToString(), cursor);
                    buffer.Remove(cursor, removeTo - cursor);
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.LeftArrow:
                    if (cursor > 0)
                    {
                        cursor = TextWidth.MoveLeft(buffer.ToString(), cursor);
                        Redraw(mode, buffer, cursor);
                    }

                    break;
                case ConsoleKey.RightArrow:
                    if (cursor < buffer.Length)
                    {
                        cursor = TextWidth.MoveRight(buffer.ToString(), cursor);
                        Redraw(mode, buffer, cursor);
                    }

                    break;
                case ConsoleKey.Home:
                    cursor = 0;
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.End:
                    cursor = buffer.Length;
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.UpArrow:
                    RecallHistory(-1, buffer, ref cursor, ref historyIndex, ref draft);
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.DownArrow:
                    RecallHistory(1, buffer, ref cursor, ref historyIndex, ref draft);
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.Escape:
                    buffer.Clear();
                    cursor = 0;
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.A when key.Modifiers == ConsoleModifiers.Control:
                    cursor = 0;
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.E when key.Modifiers == ConsoleModifiers.Control:
                    cursor = buffer.Length;
                    Redraw(mode, buffer, cursor);
                    break;
                case ConsoleKey.U when key.Modifiers == ConsoleModifiers.Control:
                    buffer.Clear();
                    cursor = 0;
                    Redraw(mode, buffer, cursor);
                    break;
                default:
                    if (!char.IsControl(key.KeyChar))
                    {
                        var atEnd = cursor == buffer.Length;
                        buffer.Insert(cursor, key.KeyChar);
                        cursor++;
                        if (atEnd && FitsOnLine(mode, buffer))
                        {
                            Console.Write(key.KeyChar);
                        }
                        else
                        {
                            Redraw(mode, buffer, cursor);
                        }
                    }

                    break;
            }
        }
    }

    private void Remember(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_history.Count > 0
            && string.Equals(_history[^1], text, StringComparison.Ordinal))
        {
            return;
        }

        _history.Add(text);
        if (_history.Count > MaximumHistory)
        {
            _history.RemoveAt(0);
        }
    }

    private void RecallHistory(
        int delta,
        StringBuilder buffer,
        ref int cursor,
        ref int historyIndex,
        ref string draft)
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (historyIndex == _history.Count)
        {
            draft = buffer.ToString();
        }

        var next = historyIndex + delta;
        if (next < 0 || next > _history.Count)
        {
            return;
        }

        historyIndex = next;
        buffer.Clear();
        buffer.Append(historyIndex == _history.Count ? draft : _history[historyIndex]);
        cursor = buffer.Length;
    }

    private static List<ConsoleKeyInfo> ReadAvailableKeys()
    {
        var burst = new List<ConsoleKeyInfo> { Console.ReadKey(intercept: true) };
        while (Console.KeyAvailable)
        {
            burst.Add(Console.ReadKey(intercept: true));
        }

        return burst;
    }

    private static void InsertChars(
        StringBuilder buffer,
        ref int cursor,
        List<ConsoleKeyInfo> burst)
    {
        var text = new StringBuilder();
        foreach (var key in burst)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                text.Append('\n');
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }

        buffer.Insert(cursor, text.ToString());
        cursor += text.Length;
    }

    private static bool FitsOnLine(bool planMode, StringBuilder buffer)
    {
        var width = LineWidth();
        var promptColumns = TextWidth.Measure(PlainPrompt(planMode));
        return promptColumns + TextWidth.Measure(buffer.ToString()) < width;
    }

    private static void Redraw(bool planMode, StringBuilder buffer, int cursor)
    {
        var width = LineWidth();
        var prompt = PlainPrompt(planMode);
        var promptColumns = TextWidth.Measure(prompt);
        var textWidth = Math.Max(width - promptColumns, 8);
        var (start, visible) = TextWidth.Window(buffer.ToString(), cursor, textWidth);
        var cursorColumns = TextWidth.Measure(
            visible.AsSpan(0, Math.Clamp(cursor - start, 0, visible.Length)));

        Console.Write('\r');
        Console.Write(new string(' ', width));
        Console.Write('\r');
        WritePrompt(planMode);
        Console.Write(visible);

        var screenColumn = promptColumns + cursorColumns;
        try
        {
            Console.SetCursorPosition(screenColumn, Console.CursorTop);
        }
        catch (IOException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static int LineWidth()
    {
        try
        {
            return Math.Max(Console.WindowWidth - 1, 8);
        }
        catch (IOException)
        {
            return 80;
        }
    }

    private static string PlainPrompt(bool planMode) =>
        planMode ? "  plan > " : "  work > ";

    private static void WritePrompt(bool planMode)
    {
        var color = planMode ? Theme.Plan : Theme.Work;
        var mode = planMode ? "plan" : "work";
        AnsiConsole.Markup($"[{Theme.Chrome}]  [/][{color}]{mode}[/][{Theme.Chrome}] > [/]");
    }
}
