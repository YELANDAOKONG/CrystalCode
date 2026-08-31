using System.Text;

namespace CrystalHarness.Display.Composer;

/// <summary>
/// Turns a pasted key burst into composer text.
/// </summary>
public static class ComposerPaste
{
    public static string FromBurst(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        ArgumentNullException.ThrowIfNull(burst);
        return BracketedPaste.Normalize(Chars(burst));
    }

    public static string Chars(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        ArgumentNullException.ThrowIfNull(burst);
        var text = new StringBuilder();
        foreach (var key in burst)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                text.Append('\n');
                continue;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                text.Append('\u001b');
                if (key.KeyChar is not ('\0' or '\u001b'))
                {
                    text.Append(key.KeyChar);
                }

                continue;
            }

            if (key.KeyChar is '\r' or '\n')
            {
                text.Append('\n');
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }

        return text.ToString();
    }
}
