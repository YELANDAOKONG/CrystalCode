using System.Text;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Splits slash-command argument text into tokens with quote and escape support.
/// A backslash escapes a double quote, or an unquoted single quote or whitespace.
/// Other backslashes are literal so Windows paths keep their separators.
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
            if (!inSingleQuotes && ch == '\\' && i + 1 < argument.Length && Escapes(argument[i + 1], inDoubleQuotes))
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

    private static bool Escapes(char next, bool inDoubleQuotes) =>
        next == '"' || (!inDoubleQuotes && (next == '\'' || char.IsWhiteSpace(next)));

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
