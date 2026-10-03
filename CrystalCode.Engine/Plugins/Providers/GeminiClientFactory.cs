using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Providers.Gemini;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class GeminiClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.Gemini;
    }

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        return new GeminiProvider(CreateOptions(settings, apiKey));
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        string apiKey) =>
        new GeminiMultimodalProvider(CreateOptions(settings, apiKey));

    private static GeminiOptions CreateOptions(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new GeminiOptions(
            apiKey,
            settings.Model,
            provider.BaseUri,
            model.Temperature,
            model.TopP,
            model.MaxTokens,
            provider.Name.Value);
    }
}
