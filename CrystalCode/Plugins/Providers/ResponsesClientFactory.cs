using Crystal.Chat;
using CrystalCode.Configuration;
using CrystalCode.Plugins.Interfaces;
using CrystalCode.Providers.Responses;

namespace CrystalCode.Plugins.Providers;

public sealed class ResponsesClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.Responses;
    }

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new ResponsesProvider(
            new ResponsesOptions(
                apiKey,
                settings.Model,
                provider.BaseUri,
                model.Temperature,
                model.TopP,
                model.MaxTokens,
                provider.Name.Value));
    }
}
