using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;
using CrystalCode.Providers.Responses;

namespace CrystalCode.Engine.Plugins.Providers;

public sealed class ResponsesClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.Responses;
    }

    /// <summary>
    /// The one built-in tool that takes a single block of text. Offering it
    /// as a free-form tool lets the model write the patch directly instead of
    /// wrapping it in JSON. A model offers it only when the operator declared
    /// <c>textTools</c>.
    /// </summary>
    internal static IReadOnlyList<TextToolBinding> TextToolsFor(ModelSettings model) =>
        model.TextTools ? [new TextToolBinding(ApplyPatchTool.ToolName, ApplyPatchTool.TextProperty)] : [];

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new ResponsesProvider(
            new ResponsesOptions(
                apiKey,
                settings.Model,
                provider.BaseUri,
                model.Temperature,
                model.TopP,
                settings.MaxOutputTokens,
                provider.Name.Value,
                serviceTier: settings.ServiceTier,
                speed: settings.Speed,
                textTools: TextToolsFor(model)));
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new ResponsesMultimodalProvider(
            new ResponsesOptions(
                apiKey,
                settings.Model,
                provider.BaseUri,
                model.Temperature,
                model.TopP,
                settings.MaxOutputTokens,
                provider.Name.Value,
                serviceTier: settings.ServiceTier,
                speed: settings.Speed,
                textTools: TextToolsFor(model)));
    }
}
