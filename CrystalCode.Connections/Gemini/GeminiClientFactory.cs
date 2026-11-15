using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Providers.Gemini;

namespace CrystalCode.Connections.Gemini;

public sealed class GeminiClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.Gemini;
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
        var options = CreateOptions(settings, credential);
        return new GeminiProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        ProviderCredential credential)
    {
        var options = CreateOptions(settings, credential);
        return new GeminiMultimodalProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }

    private static GeminiOptions CreateOptions(HarnessSettings settings, ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.ApiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new GeminiOptions(
            credential.ApiKey,
            settings.Model,
            provider.BaseUri,
            maxTokens: settings.MaxOutputTokens,
            vendorName: provider.Name.Value,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed,
            modelOptions: model.Options);
    }
}
