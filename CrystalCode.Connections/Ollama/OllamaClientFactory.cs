using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Providers.Ollama;

namespace CrystalCode.Connections.Ollama;

public sealed class OllamaClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.Ollama;
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
        var options = CreateOptions(settings, credential);
        return new OllamaProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        ProviderCredential credential)
    {
        var options = CreateOptions(settings, credential);
        return new OllamaMultimodalProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }

    private static OllamaOptions CreateOptions(HarnessSettings settings, ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new OllamaOptions(
            settings.Model,
            provider.BaseUri,
            credential.ApiKey,
            maxTokens: settings.MaxOutputTokens,
            vendorName: provider.Name.Value,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed,
            modelOptions: model.Options);
    }
}
