using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Providers.Anthropic;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class AnthropicClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.Anthropic;
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
        return new AnthropicProvider(
            new AnthropicOptions(
                apiKey,
                settings.Model,
                provider.BaseUri,
                model.Temperature,
                model.TopP,
                model.MaxTokens,
                provider.Name.Value));
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
        return new AnthropicMultimodalProvider(
            new AnthropicOptions(
                apiKey,
                settings.Model,
                provider.BaseUri,
                model.Temperature,
                model.TopP,
                model.MaxTokens,
                provider.Name.Value));
    }
}
