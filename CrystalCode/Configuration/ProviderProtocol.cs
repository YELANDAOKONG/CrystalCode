namespace CrystalCode.Configuration;

/// <summary>
/// Selects the wire protocol used by one provider entry.
/// </summary>
public sealed record ProviderProtocol
{
    public static ProviderProtocol DeepSeek { get; } = new("deepseek");

    public static ProviderProtocol OpenAI { get; } = new("openai");

    public static ProviderProtocol Responses { get; } = new("responses");

    public static ProviderProtocol Anthropic { get; } = new("anthropic");

    public ProviderProtocol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static ProviderProtocol Parse(string value)
    {
        var protocol = new ProviderProtocol(value);
        if (protocol == DeepSeek
            || protocol == OpenAI
            || protocol == Responses
            || protocol == Anthropic)
        {
            return protocol;
        }

        throw new ArgumentException(
            "Provider protocol must be deepseek, openai, responses, or anthropic.",
            nameof(value));
    }

    public override string ToString() => Value;
}
