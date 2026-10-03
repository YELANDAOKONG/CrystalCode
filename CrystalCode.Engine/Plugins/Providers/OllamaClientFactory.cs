using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Providers.Ollama;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class OllamaClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.Ollama;
    }

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new OllamaProvider(CreateOptions(settings, apiKey));
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        string apiKey) =>
        new OllamaMultimodalProvider(CreateOptions(settings, apiKey));

    private static OllamaOptions CreateOptions(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new OllamaOptions(
            settings.Model,
            provider.BaseUri,
            apiKey,
            model.Temperature,
            model.TopP,
            model.MaxTokens,
            provider.Name.Value);
    }
}
