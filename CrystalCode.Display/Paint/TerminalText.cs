using System.Text;

namespace CrystalCode.Display.Paint;

/// <summary>
/// Removes terminal controls before text becomes frame rows.
/// A complete block collapses carriage returns. A stream never rewinds
/// characters already handed to the live card, so finished rows stay put.
/// </summary>
public static class TerminalText
{
    private const int MaxSequenceLength = 64;

    private const byte Idle = 0;

    private const byte Escape = 1;

    private const byte Csi = 2;

    private const byte Osc = 3;

    public struct StreamState
    {
        internal byte _kind;
        internal byte _length;

        public void Reset()
        {
            _kind = Idle;
            _length = 0;
        }

        internal bool IsIdle => _kind == Idle;
    }

    public static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IsClean(text))
        {
            return text;
        }

        var stripped = Strip(text);
        return stripped.IndexOf('\r') < 0 ? stripped : CollapseCarriageReturns(stripped);
    }

    public static string SanitizeStream(string text, ref StreamState state)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (state.IsIdle && IsClean(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var kind = state._kind;
        var length = state._length;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            switch (kind)
            {
                case Escape:
                    kind = ch switch
                    {
                        '[' => Csi,
                        ']' or 'P' or 'X' or '^' or '_' => Osc,
                        _ => Idle
                    };
                    length = 0;
                    continue;
                case Csi:
                    length++;
                    if (length > MaxSequenceLength || ch is >= '@' and <= '~')
                    {
                        kind = Idle;
                        length = 0;
                    }

                    continue;
                case Osc:
                    length++;
                    if (ch == '\u0007' || length > MaxSequenceLength)
                    {
                        kind = Idle;
                        length = 0;
                        continue;
                    }

                    if (ch == '\u001b')
                    {
                        kind = Escape;
                        length = 0;
                    }

                    continue;
                default:
                    break;
            }

            if (ch == '\u001b')
            {
                kind = Escape;
                length = 0;
                continue;
            }

            if (ch == '\r')
            {
                builder.Append('\n');
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            if (ch == '\u007f' || (ch < ' ' && ch is not ('\n' or '\t')))
            {
                continue;
            }

            builder.Append(ch);
        }

        state._kind = kind;
        state._length = length;
        return builder.ToString();
    }

    private static bool IsClean(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch is '\n' or '\t')
            {
                continue;
            }

            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                    continue;
                }

                return false;
            }

            if (ch < ' ' || ch == '\u007f')
            {
                return false;
            }
        }

        return true;
    }

    private static string Strip(string text)
    {
        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (ch == '\u001b')
            {
                i = SkipEscape(text, i);
                continue;
            }

            if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                builder.Append('\n');
                i += 2;
                continue;
            }

            if (ch == '\u007f' || (ch < ' ' && ch is not ('\n' or '\t' or '\r')))
            {
                i++;
                continue;
            }

            builder.Append(ch);
            i++;
        }

        return builder.ToString();
    }

    private static int SkipEscape(string text, int index)
    {
        if (index + 1 >= text.Length)
        {
            return text.Length;
        }

        var next = text[index + 1];
        if (next == '[')
        {
            return SkipUntil(text, index + 2, static ch => ch is >= '@' and <= '~');
        }

        if (next is ']' or 'P' or 'X' or '^' or '_')
        {
            return SkipStringSequence(text, index + 2);
        }

        return Math.Min(text.Length, index + 2);
    }

    private static int SkipUntil(string text, int index, Func<char, bool> done)
    {
        var length = 0;
        while (index < text.Length && length <= MaxSequenceLength && !done(text[index]))
        {
            index++;
            length++;
        }

        if (index < text.Length && length <= MaxSequenceLength)
        {
            index++;
        }

        return index;
    }

    private static int SkipStringSequence(string text, int index)
    {
        var length = 0;
        while (index < text.Length && length <= MaxSequenceLength)
        {
            var ch = text[index];
            if (ch == '\u0007')
            {
                return index + 1;
            }

            if (ch == '\u001b')
            {
                return Math.Min(text.Length, index + (index + 1 < text.Length && text[index + 1] == '\\' ? 2 : 1));
            }

            index++;
            length++;
        }

        return index;
    }

    private static string CollapseCarriageReturns(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lineStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n')
            {
                continue;
            }

            AppendCollapsedLine(builder, text, lineStart, i);
            if (i < text.Length)
            {
                builder.Append('\n');
            }

            lineStart = i + 1;
        }

        return builder.ToString();
    }

    private static void AppendCollapsedLine(StringBuilder builder, string text, int start, int end)
    {
        var segment = start;
        var chosen = start;
        var chosenLength = 0;
        for (var i = start; i <= end; i++)
        {
            if (i != end && text[i] != '\r')
            {
                continue;
            }

            var length = i - segment;
            if (length > 0)
            {
                chosen = segment;
                chosenLength = length;
            }

            segment = i + 1;
        }

        if (chosenLength > 0)
        {
            builder.Append(text, chosen, chosenLength);
        }
    }
}
