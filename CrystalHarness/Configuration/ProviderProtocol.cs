namespace CrystalHarness.Configuration;

/// <summary>
/// Selects the Chat Completions dialect used by one provider entry.
/// </summary>
public sealed record ProviderProtocol
{
    public static ProviderProtocol DeepSeek { get; } = new("deepseek");

    public static ProviderProtocol OpenAI { get; } = new("openai");

    public ProviderProtocol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static ProviderProtocol Parse(string value)
    {
        var protocol = new ProviderProtocol(value);
        if (protocol == DeepSeek || protocol == OpenAI)
        {
            return protocol;
        }

        throw new ArgumentException(
            "Provider protocol must be deepseek or openai.",
            nameof(value));
    }

    public override string ToString() => Value;
}
