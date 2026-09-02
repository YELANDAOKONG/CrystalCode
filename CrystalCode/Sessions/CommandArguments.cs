using System.Text;

namespace CrystalCode.Sessions;

/// <summary>
/// Splits slash-command argument text into tokens with quote and escape support.
/// </summary>
public static class CommandArguments
{
    public static IReadOnlyList<string> Split(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        for (var i = 0; i < argument.Length; i++)
        {
            var ch = argument[i];
            if (inDoubleQuotes && ch == '\\' && i + 1 < argument.Length)
            {
                current.Append(argument[++i]);
                continue;
            }

            if (!inSingleQuotes && !inDoubleQuotes && ch == '\\' && i + 1 < argument.Length)
            {
                current.Append(argument[++i]);
                continue;
            }

            if (!inDoubleQuotes && ch == '\'')
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (!inSingleQuotes && ch == '"')
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (!inSingleQuotes && !inDoubleQuotes && char.IsWhiteSpace(ch))
            {
                Flush(current, tokens);
                continue;
            }

            current.Append(ch);
        }

        if (inSingleQuotes || inDoubleQuotes)
        {
            throw new ArgumentException("Command arguments contain an unclosed quote.");
        }

        Flush(current, tokens);
        return tokens;
    }

    private static void Flush(StringBuilder current, List<string> tokens)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }
}
