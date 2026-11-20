using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Plugins.Clients;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Copies host endpoint facts into the plugin client contract.</summary>
internal static class PluginClientRequests
{
    public static PluginClientRequest From(HarnessSettings settings, string apiKey) =>
        From(settings, new ProviderCredential(apiKey));

    public static PluginClientRequest From(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext>? requestContext = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new PluginClientRequest(
            protocol: provider.Protocol.Value,
            provider: provider.Name.Value,
            model: settings.Model,
            baseUri: provider.BaseUri,
            apiKey: credential.ApiKey,
            requiresApiKey: provider.RequiresApiKey,
            organization: provider.Organization,
            project: provider.Project,
            maxTokens: settings.MaxOutputTokens,
            tokenLimit: provider.TokenLimit.Value,
            replayReasoningContent: provider.ReplayReasoningContent,
            imageInput: model.ImageInput,
            audioInput: model.AudioInput,
            videoInput: model.VideoInput,
            fileInput: model.FileInput,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed,
            modelOptions: model.Options,
            httpHeaders: settings.EffectiveHttpHeaders)
        {
            Handler = credential.Handler,
            RequestContext = requestContext
        };
    }
}
