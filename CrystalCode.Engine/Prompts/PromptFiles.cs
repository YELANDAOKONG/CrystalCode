namespace CrystalCode.Engine.Prompts;

internal static class PromptFiles
{
    private static readonly string[] Extensions = [".md", ".txt"];

    public static string? ReadNamed(string directory, string name) =>
        ReadNamed(directory, name, keepTrailingNewlines: false);

    public static string? ReadAttachment(string directory, string name) =>
        ReadNamed(directory, name, keepTrailingNewlines: true);

    public static bool TryRead(string path, out string text) =>
        TryRead(path, keepTrailingNewlines: false, out text);

    internal static string TrimKeepingTrailingNewlines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var end = text.Length;
        while (end > start
            && char.IsWhiteSpace(text[end - 1])
            && text[end - 1] is not '\n' and not '\r')
        {
            end--;
        }

        if (start >= end)
        {
            return string.Empty;
        }

        var slice = text[start..end];
        return string.IsNullOrWhiteSpace(slice) ? string.Empty : slice;
    }

    private static string? ReadNamed(string directory, string name, bool keepTrailingNewlines)
    {
        foreach (var extension in Extensions)
        {
            var path = Path.Combine(directory, name + extension);
            if (TryRead(path, keepTrailingNewlines, out var text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool TryRead(string path, bool keepTrailingNewlines, out string text)
    {
        text = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(path);
            var normalized = keepTrailingNewlines ? TrimKeepingTrailingNewlines(raw) : raw.Trim();
            if (normalized.Length == 0)
            {
                return false;
            }

            text = normalized;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
