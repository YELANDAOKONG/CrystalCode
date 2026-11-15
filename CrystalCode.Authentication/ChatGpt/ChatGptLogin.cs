using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// The browser sign-in: authorization code with PKCE and a loopback callback,
/// or the operator pasting the callback address when the browser cannot reach
/// this machine.
/// </summary>
internal sealed class ChatGptLogin
{
    private readonly ChatGptTokenClient _tokens;
    private readonly IdTokenValidator _idTokens;
    private readonly TimeProvider _clock;
    private readonly int _preferredPort;
    private readonly string _hostId;

    public ChatGptLogin(
        ChatGptTokenClient tokens,
        IdTokenValidator idTokens,
        TimeProvider clock,
        int preferredPort,
        string hostId)
    {
        _tokens = tokens;
        _idTokens = idTokens;
        _clock = clock;
        _preferredPort = preferredPort;
        _hostId = hostId;
    }

    public async Task RunAsync(
        IAuthenticationVault vault,
        ILoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        var existing = await ReadExistingAsync(vault, cancellationToken).ConfigureAwait(false);
        var clientId = existing?.ClientId ?? ChatGptEndpoints.RegistrationClientId;
        var pkce = Pkce.Create();

        using var callback = LoopbackCallback.Start(_preferredPort);
        var link = BuildAuthorizeUri(clientId, pkce, callback.RedirectUri);
        await interaction.ShowAsync("Continue with ChatGPT in your browser.", cancellationToken)
            .ConfigureAwait(false);
        if (!await interaction.OpenLinkAsync(link, cancellationToken).ConfigureAwait(false))
        {
            await interaction.ShowAsync(
                "Open this address in a browser:" + Environment.NewLine + link,
                cancellationToken).ConfigureAwait(false);
        }

        var returned = await WaitForCallbackAsync(callback, interaction, cancellationToken)
            .ConfigureAwait(false);
        var issuedClientId = Validate(returned, pkce, clientId, existing is not null);

        ChatGptTokenResponse token;
        try
        {
            token = await _tokens.ExchangeCodeAsync(
                issuedClientId,
                returned.Code!,
                pkce.Verifier,
                callback.RedirectUri.ToString(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (TokenEndpointException exception)
        {
            throw new AuthenticationFailedException(
                "ChatGPT did not accept the sign-in. " + exception.Message);
        }

        if (!token.Scopes.Contains(ChatGptEndpoints.PlanUsageScope, StringComparer.Ordinal))
        {
            throw new AuthenticationFailedException(
                "ChatGPT plan usage was not granted to this sign-in. "
                + "Allow it for this app in ChatGPT settings, then sign in again.");
        }

        if (token.IdToken is null)
        {
            throw new AuthenticationFailedException("The sign-in returned no ID token.");
        }

        var identity = await _idTokens.ValidateAsync(
            token.IdToken,
            issuedClientId,
            pkce.Nonce,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null
            && !string.Equals(existing.Subject, identity.Subject, StringComparison.Ordinal))
        {
            throw new AuthenticationFailedException(
                "That sign-in is for a different ChatGPT account than the one saved for this provider. "
                + "Run logout first to replace it.");
        }

        var now = _clock.GetUtcNow();
        var record = new ChatGptCredentialRecord
        {
            Email = identity.Email,
            Issuer = identity.Issuer,
            Subject = identity.Subject,
            ClientId = issuedClientId,
            IdToken = token.IdToken,
            AccessToken = token.AccessToken,
            RefreshToken = token.RefreshToken,
            Scopes = token.Scopes,
            ExpiresAt = now.AddSeconds(token.ExpiresInSeconds),
            SavedAt = now
        };
        await vault.WriteAsync(record.ToJson(), cancellationToken).ConfigureAwait(false);
        await interaction.ShowAsync("Signed in to ChatGPT.", cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ChatGptCredentialRecord?> ReadExistingAsync(
        IAuthenticationVault vault,
        CancellationToken cancellationToken)
    {
        var json = await vault.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        try
        {
            return ChatGptCredentialRecord.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            // A damaged record is replaced by registering again.
            return null;
        }
    }

    private Uri BuildAuthorizeUri(string clientId, Pkce pkce, Uri redirectUri)
    {
        var query = new List<(string Name, string Value)>
        {
            ("client_id", clientId),
            ("ext_agent_host_id", _hostId),
            ("response_type", "code"),
            ("redirect_uri", redirectUri.ToString()),
            ("scope", ChatGptEndpoints.Scope),
            ("resource", ChatGptEndpoints.Resource),
            ("state", pkce.State),
            ("nonce", pkce.Nonce),
            ("code_challenge_method", "S256"),
            ("code_challenge", pkce.Challenge)
        };
        if (clientId == ChatGptEndpoints.RegistrationClientId)
        {
            // The name hint belongs to first-time registration only.
            query.Insert(1, ("agent_name_hint", ChatGptEndpoints.AgentName));
        }

        var text = string.Join(
            '&',
            query.Select(static pair => $"{pair.Name}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{ChatGptEndpoints.AuthorizeUrl}?{text}");
    }

    private static async Task<AuthorizationCallback> WaitForCallbackAsync(
        LoopbackCallback callback,
        ILoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var browser = callback.WaitAsync(stop.Token);
        var pasted = interaction.ReadAsync(
            "If the browser is on another machine, paste the address it ended on:",
            stop.Token);
        var first = await Task.WhenAny(browser, pasted).ConfigureAwait(false);
        string text;
        try
        {
            text = first == browser
                ? (await browser.ConfigureAwait(false)).ToString()
                : await pasted.ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            await ObserveAsync(browser).ConfigureAwait(false);
            await ObserveAsync(pasted).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return AuthorizationCallback.Parse(text);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped on purpose once the other source answered.
        }
    }

    private static string Validate(
        AuthorizationCallback returned,
        Pkce pkce,
        string clientId,
        bool reauthorization)
    {
        if (!string.Equals(returned.State, pkce.State, StringComparison.Ordinal))
        {
            throw new AuthenticationFailedException(
                "The sign-in answer does not belong to this attempt.");
        }

        if (!string.IsNullOrEmpty(returned.Error))
        {
            throw new AuthenticationFailedException(
                returned.Error == "access_denied"
                    ? "The sign-in was not approved, or ChatGPT plan usage is not enabled for this account."
                    : $"The sign-in failed: {returned.Error}.");
        }

        if (string.IsNullOrEmpty(returned.Code))
        {
            throw new AuthenticationFailedException("The sign-in answer carried no code.");
        }

        if (reauthorization)
        {
            if (returned.ClientId is not null && !string.Equals(returned.ClientId, clientId, StringComparison.Ordinal))
            {
                throw new AuthenticationFailedException(
                    "The sign-in named a different client than the saved login.");
            }

            return clientId;
        }

        if (string.IsNullOrEmpty(returned.ClientId)
            || returned.ClientId == ChatGptEndpoints.RegistrationClientId)
        {
            throw new AuthenticationFailedException("The registration did not return a client.");
        }

        return returned.ClientId;
    }
}
