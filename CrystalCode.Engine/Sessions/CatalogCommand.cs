namespace CrystalCode.Engine.Sessions;

/// <summary>
/// <c>enable</c>, <c>disable</c>, or <c>show</c> for one plugin or tool set directory.
/// </summary>
public sealed record CatalogCommand(string Verb, string DirectoryName, string? Source)
{
    public static bool LooksLike(IReadOnlyList<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            return false;
        }

        if (IsVerb(parts[0]))
        {
            return true;
        }

        return parts.Count >= 2 && IsSource(parts[0]) && IsVerb(parts[1]);
    }

    public static bool TryParse(IReadOnlyList<string> parts, out CatalogCommand? command, out string error)
    {
        ArgumentNullException.ThrowIfNull(parts);
        command = null;
        error = string.Empty;
        if (!LooksLike(parts))
        {
            error = "Usage: enable|disable|show <directory>.";
            return false;
        }

        string? source = null;
        string verb;
        var nameIndex = 1;
        if (IsSource(parts[0]))
        {
            source = parts[0].ToLowerInvariant();
            verb = parts[1].ToLowerInvariant();
            nameIndex = 2;
        }
        else
        {
            verb = parts[0].ToLowerInvariant();
        }

        if (parts.Count != nameIndex + 1 || string.IsNullOrWhiteSpace(parts[nameIndex]))
        {
            error = source is null
                ? "Usage: enable|disable|show <directory>."
                : "Usage: home|project enable|disable|show <directory>.";
            return false;
        }

        command = new CatalogCommand(verb, parts[nameIndex], source);
        return true;
    }

    private static bool IsVerb(string token) =>
        token.Equals("enable", StringComparison.OrdinalIgnoreCase)
        || token.Equals("disable", StringComparison.OrdinalIgnoreCase)
        || token.Equals("show", StringComparison.OrdinalIgnoreCase);

    private static bool IsSource(string token) =>
        token.Equals("home", StringComparison.OrdinalIgnoreCase)
        || token.Equals("project", StringComparison.OrdinalIgnoreCase);
}
