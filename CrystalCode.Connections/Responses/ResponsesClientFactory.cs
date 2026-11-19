using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;
using CrystalCode.Plugins.Clients;
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

    public IStreamingChatClient Create(HarnessSettings settings, ProviderCredential credential) =>
        OpenText(settings, credential, null);

    public IStreamingChatClient Create(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext> requestContext) =>
        OpenText(settings, credential, requestContext);

    private static IStreamingChatClient OpenText(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext>? requestContext)
    {
        var options = CreateOptions(settings, credential) with { RequestContext = requestContext };
        return new ResponsesProvider(
            options,
            credential.Handler is null ? null : credential.CreateHttpClient(options.RequestTimeout),
            httpHeaders: settings.EffectiveHttpHeaders);
    }

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings,
        ProviderCredential credential) =>
        OpenMultimodal(settings, credential, null);

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext> requestContext) =>
        OpenMultimodal(settings, credential, requestContext);

    private static IStreamingMultimodalChatClient OpenMultimodal(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext>? requestContext)
    {
        var options = CreateOptions(settings, credential) with { RequestContext = requestContext };
        return new ResponsesMultimodalProvider(
            options,
            credential.Handler is null ? null : credential.CreateHttpClient(options.RequestTimeout),
            httpHeaders: settings.EffectiveHttpHeaders);
    }

    internal static ResponsesOptions CreateOptions(
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
        var shape = model.Responses;
        var accountEndpoint = credential.Handler is not null;
        var officialEndpoint = provider.BaseUri.AbsoluteUri is
            "https://api.openai.com/v1/" or "https://api.openai.com/v1";
        var chatGpt = officialEndpoint && accountEndpoint && provider.Integration == "chatgpt";
        // Honor an existing explicit choice of system-message format.
        var systemAsDeveloper = shape?.SystemAsDeveloper
            ?? (shape?.SystemAsInstructions is null && officialEndpoint);
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
            // Account endpoints reject system items and sampling fields.
            requestShape: new ResponsesRequestShape(
                shape?.SystemAsInstructions ?? accountEndpoint,
                shape?.IncludeEncryptedReasoning ?? true,
                shape?.OmitSampling ?? accountEndpoint,
                SystemAsDeveloper: systemAsDeveloper,
                CompleteOverStream: accountEndpoint && officialEndpoint,
                SessionAffinity: chatGpt && shape?.SessionAffinity != false,
                PromptCacheKey: chatGpt && shape?.PromptCacheKey == true));
    }
}
