namespace CrystalCode.Engine.Configuration;

/// <summary>
/// One catalog token for a service tier or a speed. The host stores it in
/// lowercase and does not interpret it. An unset slot is not a token.
/// <c>default</c> is an ordinary token when a model lists it.
/// </summary>
public sealed record CatalogToken
{
    public CatalogToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!TryNormalize(value, out var normalized))
        {
            throw new ArgumentException(
                "The token must start with a letter and then use only letters, digits, and hyphens.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static bool TryNormalize(string? value, out string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            token = string.Empty;
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (IsToken(normalized))
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
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
