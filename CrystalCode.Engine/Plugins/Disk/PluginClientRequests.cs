using CrystalCode.Engine.Configuration;
using CrystalCode.Plugins.Clients;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Copies host endpoint facts into the plugin client contract.</summary>
internal static class PluginClientRequests
{
    public static PluginClientRequest From(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new PluginClientRequest(
            provider.Protocol.Value,
            provider.Name.Value,
            settings.Model,
            provider.BaseUri,
            apiKey,
            provider.Organization,
            provider.Project,
            model.Temperature,
            model.TopP,
            model.MaxTokens,
            provider.ReplayReasoningContent,
            provider.TokenLimit.Value,
            model.ImageInput,
            provider.RequiresApiKey);
    }
}
