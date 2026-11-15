using CrystalCode.Engine.Configuration;

namespace CrystalCode.Connections;

/// <summary>
/// The provider definitions that ship with the product. An operator's
/// <c>providers.json</c> overlays these by name.
/// </summary>
public static class BuiltInProviders
{
    public const string DefaultModel = "deepseek-flash";

    public static IReadOnlyList<ProviderDefinition> Create() =>
    [
        new ProviderDefinition(
            ProviderNames.DeepSeek,
            ProviderProtocol.DeepSeek,
            new Uri("https://api.deepseek.com/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["deepseek-flash"] = new(
                    1_000_000,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "max"],
                    imageInput: true),
                ["deepseek-v4-flash"] = new(
                    1_000_000,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "max"],
                    imageInput: true),
                ["deepseek-v4-pro"] = new(
                    1_000_000,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "max"])
            }),
        new ProviderDefinition(
            ProviderNames.OpenAI,
            ProviderProtocol.OpenAI,
            new Uri("https://api.openai.com/v1/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["gpt-5.6-sol"] = new(400_000, imageInput: true),
                ["gpt-5.6-terra"] = new(400_000, imageInput: true),
                ["gpt-5.6-luna"] = new(400_000, imageInput: true)
            }),
        new ProviderDefinition(
            ProviderNames.Gemini,
            ProviderProtocol.Gemini,
            new Uri("https://generativelanguage.googleapis.com/v1beta/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["gemini-3.8-flash"] = new(
                    1_048_576,
                    thinking: true,
                    thinkingEfforts: ["low", "medium", "high"],
                    imageInput: true,
                    thinkingCanDisable: false),
                ["gemini-3.1-pro-preview"] = new(
                    1_048_576,
                    thinking: true,
                    thinkingEfforts: ["low", "medium", "high"],
                    imageInput: true,
                    thinkingCanDisable: false)
            }),
        new ProviderDefinition(
            ProviderNames.Ollama,
            ProviderProtocol.Ollama,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["qwen3:8b"] = new(4_096, thinking: true)
            },
            requiresApiKey: false)
    ];
}
