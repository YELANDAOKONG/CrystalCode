using Crystal.Chat;

using CrystalHarness.Providers.DeepSeek;
using CrystalHarness.Providers.OpenAI;

namespace CrystalHarness.Configuration;

/// <summary>
/// Constructs a streaming chat client from a catalog entry and model settings.
/// </summary>
public static class ChatClientFactory
{
    public static IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        if (provider.Protocol == ProviderProtocol.DeepSeek)
        {
            return new DeepSeekProvider(
                new DeepSeekOptions(
                    apiKey,
                    settings.Model,
                    provider.BaseUri,
                    model.Temperature,
                    model.TopP,
                    model.MaxTokens));
        }

        if (provider.Protocol == ProviderProtocol.OpenAI)
        {
            return new OpenAIProvider(
                new OpenAIOptions(
                    apiKey,
                    settings.Model,
                    provider.BaseUri,
                    provider.Organization,
                    provider.Project,
                    model.Temperature,
                    model.TopP,
                    model.MaxTokens,
                    provider.ReplayReasoningContent,
                    useMaxCompletionTokens: provider.TokenLimit == TokenLimitStyle.MaxCompletionTokens,
                    vendorName: provider.Name.Value));
        }

        throw new NotSupportedException(
            $"Provider protocol '{provider.Protocol.Value}' is not supported.");
    }
}
