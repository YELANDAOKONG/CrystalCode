using Crystal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Constructs a streaming chat client from the in-process plugin table.
/// </summary>
public static class ChatClientFactory
{
    public static IStreamingChatClient Create(
        HarnessSettings settings,
        string apiKey,
        PluginRegistry? registry = null) =>
        Create(settings, new ProviderCredential(apiKey), registry);

    public static IStreamingChatClient Create(
        HarnessSettings settings,
        ProviderCredential credential,
        PluginRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.ApiKey);
        }
        return (registry ?? PluginRegistry.CreateBuiltIn()).CreateClient(settings, credential);
    }
}
