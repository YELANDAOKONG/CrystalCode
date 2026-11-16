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
                    maxTokens: 393_216,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "max"],
                    imageInput: true),
                ["deepseek-v4-flash"] = new(
                    1_000_000,
                    maxTokens: 393_216,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "max"],
                    imageInput: true),
                ["deepseek-v4-pro"] = new(
                    1_000_000,
                    maxTokens: 393_216,
                    thinking: true,
                    thinkingEfforts: ["low", "high", "max"])
            }),
        new ProviderDefinition(
            ProviderNames.OpenAI,
            ProviderProtocol.OpenAI,
            new Uri("https://api.openai.com/v1/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["gpt-6.1-sol"] = OpenAIModel(thinkingCanDisable: false, ultrafast: true),
                ["gpt-6.1-terra"] = OpenAIModel(),
                ["gpt-6-astra"] = OpenAIModel(thinkingCanDisable: false, ultrafast: true),
                ["gpt-6-sol"] = OpenAIModel(),
                ["gpt-6-luna"] = OpenAIModel()
            }),
        new ProviderDefinition(
            ProviderNames.Gemini,
            ProviderProtocol.Gemini,
            new Uri("https://generativelanguage.googleapis.com/v1beta/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["gemini-4-carbon"] = Gemini4Model(["low", "medium", "high"]),
                ["gemini-4-argon"] = Gemini4Model(["low", "medium", "high"]),
                ["gemini-4-hydrogen"] = Gemini4Model(["low", "medium", "high", "xhigh"])
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

    private static ModelSettings OpenAIModel(
        bool thinkingCanDisable = true,
        bool ultrafast = false) => new(
        1_050_000,
        contextWindows: [272_000, 1_050_000],
        maxTokens: 128_000,
        thinking: true,
        thinkingEfforts: ["low", "medium", "high", "xhigh", "max"],
        thinkingCanDisable: thinkingCanDisable,
        imageInput: true,
        serviceTiers: ["auto", "default", "flex"],
        speeds: ultrafast ? ["fast", "ultrafast"] : ["fast"]);

    private static ModelSettings Gemini4Model(IReadOnlyList<string> efforts) => new(
        1_048_576,
        maxTokens: 65_536,
        thinking: true,
        thinkingEfforts: efforts,
        thinkingCanDisable: false,
        imageInput: true,
        audioInput: true,
        videoInput: true,
        serviceTiers: ["standard", "flex", "priority"]);
}
