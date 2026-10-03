using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Constructs the optional image-input client from the in-process plugin table.
/// </summary>
public static class MultimodalChatClientFactory
{
    public static IStreamingMultimodalChatClient? Create(
        HarnessSettings settings,
        string apiKey,
        PluginRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        return (registry ?? PluginRegistry.CreateBuiltIn())
            .CreateMultimodalClient(settings, apiKey);
    }
}
