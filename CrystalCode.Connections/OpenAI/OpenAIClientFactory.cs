using Crystal.Chat;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Plugins.Clients;
using CrystalCode.Providers.OpenAI;

namespace CrystalCode.Connections.OpenAI;

/// <summary>
/// Builds the built-in OpenAI-compatible streaming client.
/// </summary>
public sealed class OpenAIClientFactory : IChatClientFactory
{
    public bool CanCreate(ProviderProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        return protocol == ProviderProtocol.OpenAI;
    }

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
        return new OpenAIProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }

    public IStreamingMultimodalChatClient CreateMultimodal(HarnessSettings settings, ProviderCredential credential) =>
        OpenMultimodal(settings, credential, null);

    public IStreamingMultimodalChatClient CreateMultimodal(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext> requestContext) =>
        OpenMultimodal(settings, credential, requestContext);

    private static IStreamingMultimodalChatClient OpenMultimodal(
        HarnessSettings settings, ProviderCredential credential, Func<ProviderRequestContext>? requestContext)
    {
        var options = CreateOptions(settings, credential) with { RequestContext = requestContext };
        return new OpenAIMultimodalProvider(options, CredentialHttp.Client(credential, options.RequestTimeout), settings.EffectiveHttpHeaders);
    }

    private static OpenAIOptions CreateOptions(HarnessSettings settings, ProviderCredential credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        if (settings.ActiveProvider.RequiresApiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.ApiKey);
        }
        var provider = settings.ActiveProvider;
        var model = settings.ActiveModel;
        return new OpenAIOptions(
            credential.ApiKey,
            settings.Model,
            provider.BaseUri,
            provider.Organization,
            provider.Project,
            maxTokens: settings.MaxOutputTokens,
            replayReasoningContent: provider.ReplayReasoningContent,
            useMaxCompletionTokens: provider.TokenLimit == TokenLimitStyle.MaxCompletionTokens,
            vendorName: provider.Name.Value,
            serviceTier: settings.ServiceTier,
            speed: settings.Speed,
            requireAssistantContent: provider.RequireAssistantContent,
            modelOptions: model.Options);
    }
}
