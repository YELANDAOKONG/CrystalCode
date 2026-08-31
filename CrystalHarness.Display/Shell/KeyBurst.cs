using System.Text;

namespace CrystalHarness.Display.Shell;

/// <summary>
/// Reads one console key burst. Wait after ESC only when nothing follows
/// or the CSI/mouse sequence is still incomplete.
/// </summary>
public static class KeyBurst
{
    private const char EscapeChar = '\u001b';
    private const char CsiIntro = '[';
    private const char Ss3Intro = 'O';
    private const char X10Final = 'M';
    private const char CsiFinalMin = '@';
    private const char CsiFinalMax = '~';
    private const int X10PayloadLength = 3;

    public static bool IsEscape(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.Escape || key.KeyChar == EscapeChar;

    public static bool NeedsEscapeHold(IReadOnlyList<ConsoleKeyInfo> burst, bool moreAvailable)
    {
        ArgumentNullException.ThrowIfNull(burst);
        if (moreAvailable || burst.Count == 0)
        {
            return false;
        }

        return IsIncompleteSequence(burst);
    }

    public static async Task<List<ConsoleKeyInfo>> ReadAsync(
        Func<bool> available,
        Func<ConsoleKeyInfo> read,
        Func<CancellationToken, Task> hold,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(hold);

        var burst = new List<ConsoleKeyInfo> { read() };
        if (NeedsEscapeHold(burst, available()))
        {
            await hold(cancellationToken).ConfigureAwait(false);
        }

        Drain(burst, available, read);
        if (burst.Count > 1 && NeedsEscapeHold(burst, available()))
        {
            await hold(cancellationToken).ConfigureAwait(false);
            Drain(burst, available, read);
        }

        return burst;
    }

    private static void Drain(
        List<ConsoleKeyInfo> burst,
        Func<bool> available,
        Func<ConsoleKeyInfo> read)
    {
        while (available())
        {
            burst.Add(read());
        }
    }

    private static bool IsIncompleteSequence(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        if (!IsEscape(burst[0]))
        {
            return false;
        }

        return TrailingEscapeIsIncomplete(Chars(burst));
    }

    private static bool TrailingEscapeIsIncomplete(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] != EscapeChar)
            {
                index++;
                continue;
            }

            index++;
            if (index >= text.Length)
            {
                return true;
            }

            var intro = text[index];
            switch (intro)
            {
                case CsiIntro:
                    index++;
                    if (index >= text.Length)
                    {
                        return true;
                    }

                    if (text[index] == X10Final)
                    {
                        if (index + X10PayloadLength >= text.Length)
                        {
                            return true;
                        }

                        index += 1 + X10PayloadLength;
                        break;
                    }

                    while (index < text.Length && !IsCsiFinal(text[index]))
                    {
                        index++;
                    }

                    if (index >= text.Length)
                    {
                        return true;
                    }

                    index++;
                    break;
                case Ss3Intro:
                    index++;
                    if (index >= text.Length)
                    {
                        return true;
                    }

                    index++;
                    break;
                default:
                    index++;
                    break;
            }
        }

        return false;
    }

    private static bool IsCsiFinal(char value) =>
        value is >= CsiFinalMin and <= CsiFinalMax;

    private static string Chars(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        var text = new StringBuilder();
        foreach (var key in burst)
        {
            if (key.Key == ConsoleKey.Escape)
            {
                text.Append(EscapeChar);
                if (key.KeyChar is not ('\0' or EscapeChar))
                {
                    text.Append(key.KeyChar);
                }

                continue;
            }

            if (key.KeyChar != '\0')
            {
                text.Append(key.KeyChar);
            }
        }

        return text.ToString();
    }
}
