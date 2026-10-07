namespace CrystalCode.Plugins.Placeholders;

/// <summary>
/// Name rules for a plugin prompt placeholder. The token is
/// <c>{{name}}</c>, case-insensitive.
/// </summary>
public static class PluginPlaceholderNames
{
    public const int MaximumLength = 64;

    public static bool IsName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumLength)
        {
            return false;
        }

        if (!char.IsAsciiLetter(name[0]))
        {
            return false;
        }

        for (var index = 1; index < name.Length; index++)
        {
            var character = name[index];
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
