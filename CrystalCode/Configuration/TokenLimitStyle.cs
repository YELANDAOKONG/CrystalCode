namespace CrystalCode.Configuration;

/// <summary>
/// Chooses which Chat Completions field carries the output-token cap.
/// Non-Chat-Completions protocols retain a value for configuration round trips.
/// </summary>
public sealed record TokenLimitStyle
{
    public static TokenLimitStyle MaxTokens { get; } = new("max_tokens");

    public static TokenLimitStyle MaxCompletionTokens { get; } = new("max_completion_tokens");

    public TokenLimitStyle(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static TokenLimitStyle Parse(string value)
    {
        var style = new TokenLimitStyle(value);
        if (style == MaxTokens || style == MaxCompletionTokens)
        {
            return style;
        }

        throw new ArgumentException(
            "Token limit must be max_tokens or max_completion_tokens.",
            nameof(value));
    }

    public static TokenLimitStyle ForProtocol(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.DeepSeek
            || protocol == ProviderProtocol.Anthropic
            ? MaxTokens
            : MaxCompletionTokens;
    }

    public override string ToString() => Value;
}
