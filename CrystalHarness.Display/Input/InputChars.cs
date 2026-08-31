using System.Text;

namespace CrystalHarness.Display.Input;

/// <summary>
/// Flattens a ReadKey burst to characters. Windows VT leaves Key empty; ESC still counts.
/// </summary>
internal static class InputChars
{
    public static string From(IReadOnlyList<ConsoleKeyInfo> burst)
    {
        ArgumentNullException.ThrowIfNull(burst);
        var text = new StringBuilder();
        foreach (var key in burst)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                text.Append('\r');
                continue;
            }

            if (key.Key == ConsoleKey.Escape || key.KeyChar == '\u001b')
            {
                text.Append('\u001b');
                if (key.KeyChar is not ('\0' or '\u001b'))
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
