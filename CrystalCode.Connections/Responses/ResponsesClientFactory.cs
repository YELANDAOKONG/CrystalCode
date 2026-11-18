using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;
using CrystalCode.Providers.Responses;

namespace CrystalCode.Connections.Responses;

/// <summary>
/// Builds the Responses client. It is the one built-in protocol that can
/// send requests through an authentication integration's handler.
/// </summary>
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

    public IStreamingChatClient Create(HarnessSettings settings, string apiKey) =>
        Create(settings, new ProviderCredential(apiKey));

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        string apiKey) =>
        CreateMultimodal(settings, new ProviderCredential(apiKey));

    public IStreamingChatClient Create(HarnessSettings settings, ProviderCredential credential)
    {
        var options = CreateOptions(settings, credential);
        return new ResponsesProvider(
            options,
            credential.Handler is null ? null : credential.CreateHttpClient(options.RequestTimeout),
            httpHeaders: settings.EffectiveHttpHeaders);
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        ProviderCredential credential)
    {
        var options = CreateOptions(settings, credential);
        return new ResponsesMultimodalProvider(
            options,
            credential.Handler is null ? null : credential.CreateHttpClient(options.RequestTimeout),
            httpHeaders: settings.EffectiveHttpHeaders);
    }

    private static ResponsesOptions CreateOptions(
        HarnessSettings settings,
        ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.ApiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new ResponsesOptions(
            credential.ApiKey,
            settings.Model,
            provider.BaseUri,
            maxTokens: settings.MaxOutputTokens,
            vendorName: provider.Name.Value,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed,
            textTools: TextToolsFor(model),
            modelOptions: model.Options,
            // An integration handler marks an account endpoint. It rejects system message
            // items and sampling fields, so those default on; the model's responses
            // switches override the defaults either way.
            requestShape: new ResponsesRequestShape(
                model.Responses?.SystemAsInstructions ?? credential.Handler is not null,
                model.Responses?.IncludeEncryptedReasoning ?? true,
                model.Responses?.OmitSampling ?? credential.Handler is not null,
                SystemAsDeveloper: model.Responses?.SystemAsDeveloper ?? false));
    }
}
