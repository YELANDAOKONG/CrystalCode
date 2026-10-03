using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Providers.OpenAI;

namespace CrystalCode.Engine.Plugins.Providers;

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
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
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

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new OpenAIMultimodalProvider(
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
