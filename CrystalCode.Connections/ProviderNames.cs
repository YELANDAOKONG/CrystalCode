using CrystalCode.Engine.Configuration;

namespace CrystalCode.Connections;

/// <summary>
/// Names of the providers whose definitions ship with the product.
/// </summary>
public static class ProviderNames
{
    public static ProviderName DeepSeek { get; } = new("deepseek");

    public static ProviderName OpenAI { get; } = new("openai");

    public static ProviderName Gemini { get; } = new("gemini");

    public static ProviderName Ollama { get; } = new("ollama");
}
