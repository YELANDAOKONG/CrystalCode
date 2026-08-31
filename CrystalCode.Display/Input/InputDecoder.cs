using System.Text;

namespace CrystalCode.Display.Input;

/// <summary>
/// Turns a ReadKey burst into events. Unix parsed keys, macOS Option-as-Meta,
/// and Windows VT character streams share this path.
/// </summary>
public sealed class InputDecoder
{
    private const char EscapeChar = '\u001b';
    private const string PasteStart = "\u001b[200~";
    private const string PasteEnd = "\u001b[201~";

    private readonly StringBuilder _held = new();
    private readonly StringBuilder _paste = new();
    private bool _pasteOpen;

    public bool IsPasteOpen => _pasteOpen;

    public void Reset()
    {
        _held.Clear();
        _paste.Clear();
        _pasteOpen = false;
    }

    public IReadOnlyList<IInputEvent> Push(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        ArgumentNullException.ThrowIfNull(burst);
        if (burst.Count == 0 && _held.Length == 0 && !_pasteOpen)
        {
            return [];
        }

        if (!_pasteOpen && _held.Length == 0 && burst.Count >= 2)
        {
            var flat = InputChars.From(burst);
            if (flat.Length >= 2
                && !flat.Contains(EscapeChar, StringComparison.Ordinal)
                && HasPrintable(flat)
                && !IsOnlyEdits(burst))
            {
                return [new InputPaste(NormalizePaste(flat))];
            }
        }

        if (!_pasteOpen && _held.Length == 0 && TryParsedKeys(burst, out var parsed))
        {
            return CoalesceWheel(parsed);
        }

        var text = _held + InputChars.From(burst);
        _held.Clear();
        return CoalesceWheel(Parse(text));
    }

    private static bool TryParsedKeys(
        IReadOnlyList<ConsoleKeyInfo> burst,
        out List<IInputEvent> events)
    {
        events = [];
        if (burst.Count == 0)
        {
            return false;
        }

        foreach (var key in burst)
        {
            if (key.Key == default || key.Key == ConsoleKey.Escape || key.KeyChar == EscapeChar)
            {
                events = [];
                return false;
            }
        }

        foreach (var key in burst)
        {
            events.Add(new InputKey(key.Key, key.KeyChar, key.Modifiers));
        }

        return true;
    }

    private List<IInputEvent> Parse(string text)
    {
        var events = new List<IInputEvent>();
        var index = 0;
        while (index < text.Length)
        {
            if (_pasteOpen)
            {
                ConsumePaste(text, ref index, events);
                continue;
            }

            if (text[index] == EscapeChar)
            {
                if (!TryConsumeEscape(text, ref index, events))
                {
                    _held.Append(text.AsSpan(index));
                    return events;
                }

                continue;
            }

            if (text[index] == '\r')
            {
                events.Add(new InputKey(ConsoleKey.Enter, '\r', ConsoleModifiers.None));
                index++;
                if (index < text.Length && text[index] == '\n')
                {
                    index++;
                }

                continue;
            }

            events.Add(FromChar(text[index]));
            index++;
        }

        return events;
    }

    private void ConsumePaste(string text, ref int index, List<IInputEvent> events)
    {
        var end = text.IndexOf(PasteEnd, index, StringComparison.Ordinal);
        if (end < 0)
        {
            _paste.Append(text.AsSpan(index));
            index = text.Length;
            return;
        }

        _paste.Append(text, index, end - index);
        events.Add(new InputPaste(NormalizePaste(_paste.ToString())));
        _paste.Clear();
        _pasteOpen = false;
        index = end + PasteEnd.Length;
    }

    private bool TryConsumeEscape(string text, ref int index, List<IInputEvent> events)
    {
        if (index + 1 >= text.Length)
        {
            events.Add(new InputKey(ConsoleKey.Escape, EscapeChar, ConsoleModifiers.None));
            index++;
            return true;
        }

        var intro = text[index + 1];
        switch (intro)
        {
            case '[':
                return TryConsumeCsi(text, ref index, events);
            case 'O':
                return TryConsumeSs3(text, ref index, events);
            default:
                index += 2;
                events.Add(MetaKey(intro));
                return true;
        }
    }

    private bool TryConsumeCsi(string text, ref int index, List<IInputEvent> events)
    {
        if (text.AsSpan(index).StartsWith(PasteStart.AsSpan()))
        {
            _pasteOpen = true;
            index += PasteStart.Length;
            return true;
        }

        if (index + 2 >= text.Length)
        {
            return false;
        }

        if (text[index + 2] == '<')
        {
            return TryConsumeSgr(text, ref index, events);
        }

        if (text[index + 2] == 'M')
        {
            return TryConsumeX10(text, ref index, events);
        }

        var cursor = index + 2;
        while (cursor < text.Length && !IsCsiFinal(text[cursor]))
        {
            cursor++;
        }

        if (cursor >= text.Length)
        {
            return false;
        }

        var body = text[(index + 2)..cursor];
        var final = text[cursor];
        index = cursor + 1;
        if (TryMapCsi(body, final, out var mapped))
        {
            events.Add(mapped);
        }

        return true;
    }

    private static bool TryConsumeSs3(string text, ref int index, List<IInputEvent> events)
    {
        if (index + 2 >= text.Length)
        {
            return false;
        }

        var final = text[index + 2];
        index += 3;
        var key = final switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            _ => default
        };
        if (key != default)
        {
            events.Add(new InputKey(key, '\0', ConsoleModifiers.None));
        }

        return true;
    }

    private static bool TryConsumeSgr(string text, ref int index, List<IInputEvent> events)
    {
        var close = text.IndexOfAny(['M', 'm'], index + 3);
        if (close < 0)
        {
            return false;
        }

        var payload = text[(index + 3)..close];
        index = close + 1;
        var first = payload.IndexOf(';');
        if (first < 0 || !int.TryParse(payload.AsSpan(0, first), out var button))
        {
            return true;
        }

        if ((button & 64) == 0)
        {
            return true;
        }

        var delta = (button & 1) == 0 ? InputWheel.LineStep : -InputWheel.LineStep;
        events.Add(new InputWheel(delta));
        return true;
    }

    private static bool TryConsumeX10(string text, ref int index, List<IInputEvent> events)
    {
        if (index + 5 >= text.Length)
        {
            return false;
        }

        var button = text[index + 3] - 32;
        index += 6;
        if ((button & 64) == 0)
        {
            return true;
        }

        var delta = (button & 1) == 0 ? InputWheel.LineStep : -InputWheel.LineStep;
        events.Add(new InputWheel(delta));
        return true;
    }

    private static bool TryMapCsi(string body, char final, out IInputEvent mapped)
    {
        mapped = null!;
        var parts = body.Split(';', StringSplitOptions.None);
        var first = ParseParam(parts, 0, 1);
        var second = ParseParam(parts, 1, 1);
        var modifiers = parts.Length >= 2 ? ModifiersFrom(second) : ConsoleModifiers.None;

        if (final == '~')
        {
            return TryMapTilde(first, parts, out mapped);
        }

        if (final == 'u')
        {
            return TryMapKitty(first, second, parts, out mapped);
        }

        if (final == 'Z')
        {
            mapped = new InputKey(ConsoleKey.Tab, '\t', ConsoleModifiers.Shift);
            return true;
        }

        var arrow = final switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            _ => default
        };
        if (arrow == default)
        {
            return false;
        }

        mapped = new InputKey(arrow, '\0', modifiers);
        return true;
    }

    private static bool TryMapTilde(int first, string[] parts, out IInputEvent mapped)
    {
        mapped = null!;
        if (first == 27 && parts.Length >= 3 && int.TryParse(parts[2], out var code))
        {
            mapped = code switch
            {
                13 => new InputKey(ConsoleKey.Enter, '\r', ConsoleModifiers.None),
                9 => new InputKey(ConsoleKey.Tab, '\t', ConsoleModifiers.None),
                8 or 127 => new InputKey(ConsoleKey.Backspace, '\b', ConsoleModifiers.None),
                _ => null!
            };
            return mapped is not null;
        }

        var key = first switch
        {
            1 or 7 => ConsoleKey.Home,
            2 => ConsoleKey.Insert,
            3 => ConsoleKey.Delete,
            4 or 8 => ConsoleKey.End,
            5 => ConsoleKey.PageUp,
            6 => ConsoleKey.PageDown,
            _ => default
        };
        if (key == default)
        {
            return false;
        }

        var modifiers = parts.Length >= 2 ? ModifiersFrom(ParseParam(parts, 1, 1)) : ConsoleModifiers.None;
        mapped = new InputKey(key, '\0', modifiers);
        return true;
    }

    private static bool TryMapKitty(int first, int second, string[] parts, out IInputEvent mapped)
    {
        mapped = null!;
        var modifiers = parts.Length >= 2 ? ModifiersFrom(second) : ConsoleModifiers.None;
        mapped = first switch
        {
            9 => new InputKey(ConsoleKey.Tab, '\t', modifiers),
            13 => new InputKey(ConsoleKey.Enter, '\r', modifiers),
            127 => new InputKey(ConsoleKey.Backspace, '\b', modifiers),
            _ => null!
        };
        return mapped is not null;
    }

    private static InputKey MetaKey(char intro)
    {
        if (intro is '\b' or '\u007f')
        {
            return new InputKey(ConsoleKey.Backspace, '\b', ConsoleModifiers.Alt);
        }

        var fromChar = FromChar(intro);
        return fromChar with { Modifiers = fromChar.Modifiers | ConsoleModifiers.Alt };
    }

    private static InputKey FromChar(char value)
    {
        if (value == '\t')
        {
            return new InputKey(ConsoleKey.Tab, '\t', ConsoleModifiers.None);
        }

        if (value == '\r')
        {
            return new InputKey(ConsoleKey.Enter, '\r', ConsoleModifiers.None);
        }

        if (value == '\n')
        {
            return new InputKey(ConsoleKey.J, '\n', ConsoleModifiers.Control);
        }

        if (value is '\b' or '\u007f')
        {
            return new InputKey(ConsoleKey.Backspace, value, ConsoleModifiers.None);
        }

        if (value is >= '\u0001' and <= '\u001a')
        {
            var letter = (char)('A' + value - 1);
            return new InputKey((ConsoleKey)letter, value, ConsoleModifiers.Control);
        }

        if (value is >= 'A' and <= 'Z')
        {
            return new InputKey((ConsoleKey)value, value, ConsoleModifiers.Shift);
        }

        if (value is >= 'a' and <= 'z')
        {
            return new InputKey((ConsoleKey)char.ToUpperInvariant(value), value, ConsoleModifiers.None);
        }

        if (value is >= '0' and <= '9')
        {
            return new InputKey((ConsoleKey)value, value, ConsoleModifiers.None);
        }

        return new InputKey(default, value, ConsoleModifiers.None);
    }

    private static IReadOnlyList<IInputEvent> CoalesceWheel(List<IInputEvent> events)
    {
        if (events.Count < 2)
        {
            return events;
        }

        var ups = 0;
        var downs = 0;
        foreach (var item in events)
        {
            if (item is not InputKey key || key.Modifiers.HasFlag(ConsoleModifiers.Alt))
            {
                return events;
            }

            if (key.Key == ConsoleKey.UpArrow)
            {
                ups++;
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                downs++;
                continue;
            }

            return events;
        }

        return [new InputWheel((ups - downs) * InputWheel.LineStep)];
    }

    private static string NormalizePaste(string text)
    {
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (!char.IsControl(ch) || ch is '\n' or '\t')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static int ParseParam(string[] parts, int index, int fallback)
    {
        if (index >= parts.Length || parts[index].Length == 0)
        {
            return fallback;
        }

        return int.TryParse(parts[index], out var value) ? value : fallback;
    }

    private static ConsoleModifiers ModifiersFrom(int code)
    {
        var bits = Math.Max(code, 1) - 1;
        var modifiers = ConsoleModifiers.None;
        if ((bits & 1) != 0)
        {
            modifiers |= ConsoleModifiers.Shift;
        }

        if ((bits & 2) != 0)
        {
            modifiers |= ConsoleModifiers.Alt;
        }

        if ((bits & 4) != 0)
        {
            modifiers |= ConsoleModifiers.Control;
        }

        return modifiers;
    }

    private static bool IsCsiFinal(char value) =>
        value is >= '@' and <= '~';

    private static bool HasPrintable(string text)
    {
        foreach (var ch in text)
        {
            if (!char.IsControl(ch))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOnlyEdits(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        foreach (var key in burst)
        {
            if (key.Key is not (ConsoleKey.Backspace or ConsoleKey.Delete)
                && key.KeyChar is not ('\b' or '\u007f'))
            {
                return false;
            }
        }

        return true;
    }
}
