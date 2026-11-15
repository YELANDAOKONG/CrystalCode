using System.Text.Json;

using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// Signs in with a ChatGPT account (the Sign in with ChatGPT plan-usage flow)
/// and sends Responses requests with that account's access token.
/// </summary>
public sealed class ChatGptIntegration : IAuthenticationIntegration
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly ChatGptTokenClient _tokens;
    private readonly IdTokenValidator _idTokens;
    private readonly TimeProvider _clock;
    private readonly int _callbackPort;
    private readonly string _hostId;

    public ChatGptIntegration()
        : this(new SocketsHttpHandler(), TimeProvider.System, ChatGptEndpoints.PreferredCallbackPort, HostIdentity.Create())
    {
    }

    internal ChatGptIntegration(
        HttpMessageHandler handler,
        TimeProvider clock,
        int callbackPort,
        string hostId)
    {
        var http = new HttpClient(handler) { Timeout = RequestTimeout };
        _tokens = new ChatGptTokenClient(http);
        _idTokens = new IdTokenValidator(http, clock);
        _clock = clock;
        _callbackPort = callbackPort;
        _hostId = hostId;
    }

    public string Id => ChatGptEndpoints.IntegrationId;

    public string? Validate(ProviderDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        // The token is only good for this resource, so no other address may receive it.
        if (!ChatGptEndpoints.InferenceBase.Equals(provider.BaseUri))
        {
            return $"the ChatGPT integration sends its token only to {ChatGptEndpoints.InferenceBase}, "
                + "so baseUri must be that address.";
        }

        if (provider.Protocol != ProviderProtocol.Responses)
        {
            return "the ChatGPT integration uses the responses protocol.";
        }

        // The plan-usage endpoint rejects these request fields.
        foreach (var (name, model) in provider.Models)
        {
            if (model.MaxTokens is not null)
            {
                return $"model {name} sets maxTokens, which ChatGPT plan usage does not accept.";
            }

            if (model.Options is { ValueKind: JsonValueKind.Object } options
                && options.EnumerateObject().Any(static property => IsRejectedOption(property.Name)))
            {
                return $"model {name} sets temperature or topP, which ChatGPT plan usage does not accept.";
            }
        }

        return null;
    }

    public Task LoginAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        ILoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(interaction);
        var login = new ChatGptLogin(_tokens, _idTokens, _clock, _callbackPort, _hostId);
        return login.RunAsync(vault, interaction, cancellationToken);
    }

    public DelegatingHandler CreateHandler(ProviderDefinition provider, IAuthenticationVault vault)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(vault);
        return new ChatGptAuthHandler(provider.Name, vault, _tokens, _clock);
    }

    public async Task<string?> RevokeAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(vault);
        var json = await vault.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        ChatGptCredentialRecord record;
        try
        {
            record = ChatGptCredentialRecord.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        return await _tokens.RevokeAsync(record.ClientId, record.RefreshToken, cancellationToken)
            .ConfigureAwait(false)
            ? null
            : "ChatGPT did not confirm that the session ended. You can disconnect the app in ChatGPT settings.";
    }

    private static bool IsRejectedOption(string name) =>
        name.Equals("temperature", StringComparison.OrdinalIgnoreCase)
        || name.Equals("topP", StringComparison.OrdinalIgnoreCase);
}
