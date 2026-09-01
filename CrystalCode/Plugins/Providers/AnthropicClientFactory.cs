using Crystal.Chat;
using CrystalCode.Configuration;
using CrystalCode.Plugins.Interfaces;
using CrystalCode.Providers.Anthropic;

namespace CrystalCode.Plugins.Providers;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
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
}
