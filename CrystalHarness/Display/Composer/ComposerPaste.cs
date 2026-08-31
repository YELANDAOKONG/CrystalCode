using System.Text;

namespace CrystalHarness.Display.Composer;

/// <summary>
/// Turns a pasted key burst into composer text.
/// </summary>
internal static class ComposerPaste
{
    public static string FromBurst(IReadOnlyList<ConsoleKeyInfo> burst)
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

            if (!char.IsControl(key.KeyChar))
            {
                text.Append(key.KeyChar);
            }
        }

        return text.ToString();
    }
}
