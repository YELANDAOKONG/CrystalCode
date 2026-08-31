using Crystal.Chat;
using CrystalCode.Plugins;

namespace CrystalCode.Configuration;

/// <summary>
/// Constructs a streaming chat client from the in-process plugin table.
/// </summary>
public static class ChatClientFactory
{
    public static IStreamingChatClient Create(
        HarnessSettings settings,
        string apiKey,
        PluginRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return (registry ?? PluginRegistry.CreateBuiltIn()).CreateClient(settings, apiKey);
    }
}
