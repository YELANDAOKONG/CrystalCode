using Crystal.Multimodal.Chat;
using CrystalCode.Plugins;

namespace CrystalCode.Configuration;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return (registry ?? PluginRegistry.CreateBuiltIn())
            .CreateMultimodalClient(settings, apiKey);
    }
}
