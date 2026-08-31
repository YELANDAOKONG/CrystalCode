using Crystal.Chat;

using CrystalHarness.Configuration;
using CrystalHarness.Plugins.Interfaces;
using CrystalHarness.Providers.DeepSeek;

namespace CrystalHarness.Plugins.Providers;

/// <summary>
/// Builds the built-in DeepSeek streaming client.
/// </summary>
public sealed class DeepSeekClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.DeepSeek;
    }

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new DeepSeekProvider(
            new DeepSeekOptions(
                apiKey,
                settings.Model,
                provider.BaseUri,
                model.Temperature,
                model.TopP,
                model.MaxTokens));
    }
}
