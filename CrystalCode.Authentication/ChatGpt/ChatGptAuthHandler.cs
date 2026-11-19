using System.Net;
using System.Net.Http.Headers;

using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Plugins.Clients;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// Attaches the saved access token to each request, renews it shortly before
/// it expires, and after a 401 renews it once and sends the request again.
/// Renewal runs under the vault's cross-process lock because the refresh
/// token rotates.
/// </summary>
internal sealed class ChatGptAuthHandler : DelegatingHandler
{
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    private readonly ProviderName _provider;
    private readonly IAuthenticationVault _vault;
    private readonly ChatGptTokenClient _tokens;
    private readonly TimeProvider _clock;
    private ChatGptCredentialRecord? _cached;

    public ChatGptAuthHandler(
        ProviderName provider,
        IAuthenticationVault vault,
        ChatGptTokenClient tokens,
        TimeProvider clock)
    {
        _provider = provider;
        _vault = vault;
        _tokens = tokens;
        _clock = clock;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Buffered so a second attempt can send the same body.
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        }

        var token = await GetAccessTokenAsync(rejected: null, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var renewed = await GetAccessTokenAsync(rejected: token, cancellationToken).ConfigureAwait(false);
        using var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        if (request.Options.TryGetValue(ProviderHttpRequestOptions.AdditionalAttempt, out var prepareRetry))
        {
            prepareRetry(retry);
        }

        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetAccessTokenAsync(string? rejected, CancellationToken cancellationToken)
    {
        if (rejected is null
            && _cached is { } cached
            && cached.IsFresh(_clock.GetUtcNow(), Skew))
        {
            return cached.AccessToken;
        }

        var record = await _vault.RunExclusiveAsync(
            token => RenewAsync(rejected, token),
            cancellationToken).ConfigureAwait(false);
        _cached = record;
        return record.AccessToken;
    }

    private async Task<ChatGptCredentialRecord> RenewAsync(string? rejected, CancellationToken cancellationToken)
    {
        var json = await _vault.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            throw NotSignedIn();
        }

        var current = ChatGptCredentialRecord.Parse(json);
        // Another process may have renewed while this one waited for the lock.
        var stillUsable = rejected is null
            ? current.IsFresh(_clock.GetUtcNow(), Skew)
            : !string.Equals(current.AccessToken, rejected, StringComparison.Ordinal)
                && current.IsFresh(_clock.GetUtcNow(), Skew);
        if (stillUsable)
        {
            return current;
        }

        ChatGptTokenResponse response;
        try
        {
            response = await _tokens.RefreshAsync(current.ClientId, current.RefreshToken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TokenEndpointException exception) when (exception.IsInvalidGrant)
        {
            throw new AuthenticationFailedException(
                $"The saved login for {_provider.Value} was rejected. Run crystal login {_provider.Value}.");
        }
        catch (TokenEndpointException exception)
        {
            throw new AuthenticationFailedException(
                $"Renewing the login for {_provider.Value} failed. {exception.Message}");
        }

        var now = _clock.GetUtcNow();
        var next = current with
        {
            AccessToken = response.AccessToken,
            RefreshToken = response.RefreshToken,
            IdToken = response.IdToken ?? current.IdToken,
            Scopes = response.Scopes.Count > 0 ? response.Scopes : current.Scopes,
            ExpiresAt = now.AddSeconds(response.ExpiresInSeconds),
            SavedAt = now
        };
        await _vault.WriteAsync(next.ToJson(), cancellationToken).ConfigureAwait(false);
        return next;
    }

    private AuthenticationFailedException NotSignedIn() =>
        new($"Provider {_provider.Value} is not signed in. Run crystal login {_provider.Value}.");

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
