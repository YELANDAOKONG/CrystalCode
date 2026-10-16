namespace CrystalCode.Plugins.Status;

/// <summary>
/// Name rules for a plugin status field. The token is one lowercase word
/// in <c>statusLine</c>, case-insensitive at admission.
/// </summary>
public static class PluginStatusFieldNames
{
    public const int MaximumLength = 32;

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

        var hyphen = false;
        for (var index = 1; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '-')
            {
                if (hyphen || index == name.Length - 1)
                {
                    return false;
                }

                hyphen = true;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }

            hyphen = false;
        }

        return true;
    }
}
