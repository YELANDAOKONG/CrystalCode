namespace CrystalCode.Engine.Configuration;

/// <summary>
/// One catalog token for a service tier or a speed. The host stores it in
/// lowercase and does not interpret it. An unset slot is not a token, and
/// <see cref="Unset"/> is reserved as the word that clears a slot, so no
/// model may list it. <c>default</c> is an ordinary token when a model lists it.
/// </summary>
public sealed record CatalogToken
{
    /// <summary>The word that clears a tier or speed slot in a command or an option value.</summary>
    public const string Unset = "unset";

    public CatalogToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!TryNormalize(value, out var normalized))
        {
            throw new ArgumentException(
                "The token must start with a letter and then use only letters, digits, underscores, and hyphens, and cannot be '"
                + Unset + "'.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    /// <summary>True when <paramref name="value"/> is the clear word, ignoring case and surrounding space.</summary>
    public static bool IsUnset(string? value) =>
        string.Equals(value?.Trim(), Unset, StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalize(string? value, out string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            token = string.Empty;
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized != Unset && IsToken(normalized))
        {
            token = normalized;
            return true;
        }

        token = string.Empty;
        return false;
    }

    public override string ToString() => Value;

    private static bool IsToken(string value)
    {
        if (value.Length == 0 || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
