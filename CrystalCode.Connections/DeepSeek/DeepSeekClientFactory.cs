using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Providers.DeepSeek;

namespace CrystalCode.Connections.DeepSeek;

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

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey) =>
        Create(settings, new ProviderCredential(apiKey));

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        string apiKey) =>
        CreateMultimodal(settings, new ProviderCredential(apiKey));

    public IStreamingChatClient Create(HarnessSettings settings, ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.ApiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        var options = new DeepSeekOptions(
            credential.ApiKey,
            settings.Model,
            provider.BaseUri,
            maxTokens: settings.MaxOutputTokens,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed,
            modelOptions: model.Options);
        return new DeepSeekProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.ApiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        var options = new DeepSeekOptions(
            credential.ApiKey,
            settings.Model,
            provider.BaseUri,
            maxTokens: settings.MaxOutputTokens,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed,
            modelOptions: model.Options);
        return new DeepSeekMultimodalProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }
}
