namespace CrystalCode.Prompts;

internal static class PromptFiles
{
    private static readonly string[] Extensions = [".md", ".txt"];

    public static string? ReadNamed(string directory, string name)
    {
        foreach (var extension in Extensions)
        {
            var path = Path.Combine(directory, name + extension);
            if (TryRead(path, out var text))
            {
                return text;
            }
        }

        return null;
    }

    public static bool TryRead(string path, out string text)
    {
        text = string.Empty;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(path).Trim();
            if (raw.Length == 0)
            {
                return false;
            }

            text = raw;
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
