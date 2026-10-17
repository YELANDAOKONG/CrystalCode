namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Normalizes and cycles a model's service-tier or speed list.
/// The unset slot is null and is not one of the tokens.
/// </summary>
public static class CatalogSlots
{
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? tokens, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (tokens is null || tokens.Count == 0)
        {
            return [];
        }

        var normalized = new List<string>(tokens.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token) || !CatalogToken.TryNormalize(token, out var value))
            {
                throw new ArgumentException(
                    $"{name} must start with a letter and then use only letters, digits, underscores, and hyphens.",
                    nameof(tokens));
            }

            if (!seen.Add(value))
            {
                throw new ArgumentException($"{name} '{value}' is listed more than once.", nameof(tokens));
            }

            normalized.Add(value);
        }

        return normalized;
    }

    public static bool Contains(IReadOnlyList<string> tokens, string token)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (!CatalogToken.TryNormalize(token, out var normalized))
        {
            return false;
        }

        foreach (var allowed in tokens)
        {
            if (string.Equals(allowed, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves from unset to the first token, then through the list, then back
    /// to unset. A stored token the model no longer lists is treated as unset.
    /// </summary>
    public static string? Next(string? current, IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0)
        {
            return null;
        }

        if (current is null)
        {
            return tokens[0];
        }

        var index = -1;
        for (var position = 0; position < tokens.Count; position++)
        {
            if (string.Equals(tokens[position], current, StringComparison.Ordinal))
            {
                index = position;
                break;
            }
        }

        if (index < 0)
        {
            return tokens[0];
        }

        if (index == tokens.Count - 1)
        {
            return null;
        }

        return tokens[index + 1];
    }
}
