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
            protocol: provider.Protocol.Value,
            provider: provider.Name.Value,
            model: settings.Model,
            baseUri: provider.BaseUri,
            apiKey: apiKey,
            requiresApiKey: provider.RequiresApiKey,
            organization: provider.Organization,
            project: provider.Project,
            maxTokens: settings.MaxOutputTokens,
            tokenLimit: provider.TokenLimit.Value,
            temperature: model.Temperature,
            topP: model.TopP,
            replayReasoningContent: provider.ReplayReasoningContent,
            imageInput: model.ImageInput,
            audioInput: model.AudioInput,
            videoInput: model.VideoInput,
            fileInput: model.FileInput,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed);
    }
}
