namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Selects the wire protocol used by one provider entry.
/// </summary>
public sealed record ProviderProtocol
{
    public static ProviderProtocol DeepSeek { get; } = new("deepseek");

    public static ProviderProtocol OpenAI { get; } = new("openai");

    public static ProviderProtocol Responses { get; } = new("responses");

    public static ProviderProtocol Anthropic { get; } = new("anthropic");

    public static ProviderProtocol Gemini { get; } = new("gemini");

    public static ProviderProtocol Ollama { get; } = new("ollama");

    public ProviderProtocol(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static ProviderProtocol Parse(string value)
    {
        var protocol = new ProviderProtocol(value);
        if (protocol == DeepSeek)
        {
            return DeepSeek;
        }

        if (protocol == OpenAI)
        {
            return OpenAI;
        }

        if (protocol == Responses)
        {
            return Responses;
        }

        if (protocol == Anthropic)
        {
            return Anthropic;
        }

        if (protocol == Gemini)
        {
            return Gemini;
        }

        if (protocol == Ollama)
        {
            return Ollama;
        }

        if (IsPluginToken(protocol.Value))
        {
            return protocol;
        }

        throw new ArgumentException(
            "Provider protocol must be deepseek, openai, responses, anthropic, gemini, ollama, or a plugin protocol token.",
            nameof(value));
    }

    private static bool IsPluginToken(string value)
    {
        if (value.Length is < 1 or > 64 || !char.IsAsciiLetter(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Value;
}
