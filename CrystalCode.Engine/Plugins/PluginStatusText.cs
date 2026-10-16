using System.Text;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Reduces plugin status text to one plain line before it is published.
/// Display width stays with the front end.
/// </summary>
public static class PluginStatusText
{
    public const int MaximumTextLength = 64;

    public const int MaximumDescriptionLength = 80;

    private const int MaximumEscapeLength = 64;

    public static string Normalize(string? text, int maximumLength)
    {
        if (maximumLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maximumLength));
        var index = 0;
        var started = false;
        while (index < text.Length && builder.Length < maximumLength)
        {
            var character = text[index];
            if (character == '\u001b')
            {
                index = SkipEscape(text, index);
                continue;
            }

            if (character is '\r' or '\n' or '\t')
            {
                character = ' ';
            }
            else if (character < ' ' || character == '\u007f' || character is >= '\u0080' and <= '\u009f')
            {
                index++;
                continue;
            }

            if (character == ' ' && !started)
            {
                index++;
                continue;
            }

            builder.Append(character);
            started = true;
            index++;
        }

        var length = builder.Length;
        while (length > 0 && builder[length - 1] == ' ')
        {
            length--;
        }

        return length == builder.Length
            ? builder.ToString()
            : builder.ToString(0, length);
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
            return SkipUntil(text, index + 2, static character => character is >= '@' and <= '~');
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
        while (index < text.Length && length <= MaximumEscapeLength && !done(text[index]))
        {
            index++;
            length++;
        }

        if (index < text.Length && length <= MaximumEscapeLength)
        {
            index++;
        }

        return index;
    }

    private static int SkipStringSequence(string text, int index)
    {
        var length = 0;
        while (index < text.Length && length <= MaximumEscapeLength)
        {
            var character = text[index];
            if (character == '\u0007')
            {
                return index + 1;
            }

            if (character == '\u001b')
            {
                return Math.Min(
                    text.Length,
                    index + (index + 1 < text.Length && text[index + 1] == '\\' ? 2 : 1));
            }

            index++;
            length++;
        }

        return index;
    }
}
