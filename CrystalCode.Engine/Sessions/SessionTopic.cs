namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Normalizes a session topic: one line, collapsed whitespace, no wrapping
/// quotes or trailing period, at most <see cref="MaxLength"/> characters.
/// </summary>
public static class SessionTopic
{
    public const int MaxLength = 80;

    /// <summary>What topic naming returns when the intent is unclear.</summary>
    public const string Unclear = "New conversation";

    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static candidate => candidate.Length > 0) ?? string.Empty;
        var collapsed = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        collapsed = collapsed.Trim('"', '\'', '`', '*', '#', ' ').TrimEnd('.').Trim();
        if (collapsed.Length == 0)
        {
            return null;
        }

        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength].TrimEnd();
    }
}
