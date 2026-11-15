namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Reads and writes the <c>type</c> field of a provider entry.
/// </summary>
public static class ProviderTypeText
{
    public const string Authentication = "authentication";

    public static ProviderType Parse(string providerName, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ProviderType.Key;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "default":
            case "key":
                return ProviderType.Key;
            case Authentication:
                return ProviderType.Authentication;
            default:
                throw new InvalidOperationException(
                    $"Provider '{providerName}' has unknown type '{text.Trim()}'. "
                    + $"Use default, key, or {Authentication}.");
        }
    }
}
