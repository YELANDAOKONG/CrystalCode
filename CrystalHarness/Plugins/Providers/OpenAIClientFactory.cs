using Crystal.Chat;
using CrystalHarness.Configuration;
using CrystalHarness.Providers.OpenAI;

namespace CrystalHarness.Plugins.Providers;

/// <summary>
/// Builds the built-in OpenAI-compatible streaming client.
/// </summary>
public sealed class OpenAIClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.OpenAI;
    }

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
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
}
