namespace CrystalHarness.Configuration;

/// <summary>
/// Identifies one configured provider entry. Names are open; built-in
/// DeepSeek and OpenAI values are only well-known starters.
/// </summary>
public sealed record ProviderName
{
    public static ProviderName DeepSeek { get; } = new("deepseek");

    public static ProviderName OpenAI { get; } = new("openai");

    public ProviderName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0 || !IsValid(normalized))
        {
            throw new ArgumentException(
                "Provider name must be letters, digits, hyphen, or underscore.",
                nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static ProviderName Parse(string value) => new(value);

    public string ApiKeyEnvironmentName =>
        Value.Replace('-', '_').ToUpperInvariant() + "_API_KEY";

    public override string ToString() => Value;

    private static bool IsValid(string value)
    {
        foreach (var character in value)
        {
            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
