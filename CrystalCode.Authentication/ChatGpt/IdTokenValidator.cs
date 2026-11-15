using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CrystalCode.Engine.Authentication;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// Verifies an ID token's RS256 signature against the issuer's published
/// keys, then checks issuer, audience, expiry, and the nonce of this attempt.
/// </summary>
internal sealed class IdTokenValidator
{
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    public IdTokenValidator(HttpClient http, TimeProvider clock)
    {
        _http = http;
        _clock = clock;
    }

    public async Task<IdTokenIdentity> ValidateAsync(
        string idToken,
        string clientId,
        string nonce,
        CancellationToken cancellationToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            throw Failure("The ID token is not a signed token.");
        }

        using var header = JsonDocument.Parse(Decode(parts[0]));
        if (header.RootElement.GetStringOrNull("alg") != "RS256")
        {
            throw Failure("The ID token uses an unsupported signature algorithm.");
        }

        var keyId = header.RootElement.GetStringOrNull("kid");
        using var rsa = await LoadKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        if (!rsa.VerifyData(signed, Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw Failure("The ID token signature is not valid.");
        }

        using var payload = JsonDocument.Parse(Decode(parts[1]));
        var claims = payload.RootElement;
        var issuer = claims.GetStringOrNull("iss");
        if (issuer != ChatGptEndpoints.Issuer)
        {
            throw Failure("The ID token was issued by an unexpected party.");
        }

        if (!HasAudience(claims, clientId))
        {
            throw Failure("The ID token is for a different client.");
        }

        if (!claims.TryGetProperty("exp", out var exp)
            || !exp.TryGetInt64(out var expires)
            || DateTimeOffset.FromUnixTimeSeconds(expires) <= _clock.GetUtcNow())
        {
            throw Failure("The ID token has expired.");
        }

        if (claims.GetStringOrNull("nonce") != nonce)
        {
            throw Failure("The ID token does not match this sign-in attempt.");
        }

        var subject = claims.GetStringOrNull("sub");
        if (string.IsNullOrEmpty(subject))
        {
            throw Failure("The ID token names no account.");
        }

        return new IdTokenIdentity(issuer, subject, claims.GetStringOrNull("email"));
    }

    private async Task<RSA> LoadKeyAsync(string? keyId, CancellationToken cancellationToken)
    {
        var jwksUrl = await ReadJwksUrlAsync(cancellationToken).ConfigureAwait(false);
        var body = await _http.GetStringAsync(jwksUrl, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        foreach (var key in document.RootElement.GetProperty("keys").EnumerateArray())
        {
            if (key.GetStringOrNull("kty") != "RSA"
                || (keyId is not null && key.GetStringOrNull("kid") != keyId))
            {
                continue;
            }

            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Decode(key.GetStringOrNull("n") ?? string.Empty),
                Exponent = Decode(key.GetStringOrNull("e") ?? string.Empty)
            });
            return rsa;
        }

        throw Failure("The ID token signing key was not found.");
    }

    private async Task<string> ReadJwksUrlAsync(CancellationToken cancellationToken)
    {
        var body = await _http.GetStringAsync(ChatGptEndpoints.DiscoveryUrl, cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(body);
        var url = document.RootElement.GetStringOrNull("jwks_uri");
        if (url is null || !url.StartsWith(ChatGptEndpoints.Issuer + "/", StringComparison.Ordinal))
        {
            throw Failure("The sign-in service published no signing keys.");
        }

        return url;
    }

    private static bool HasAudience(JsonElement claims, string clientId)
    {
        if (!claims.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        return audience.ValueKind == JsonValueKind.Array
            ? audience.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == clientId)
            : audience.ValueKind == JsonValueKind.String && audience.GetString() == clientId;
    }

    private static byte[] Decode(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - (padded.Length % 4)) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private static AuthenticationFailedException Failure(string message) => new(message);
}
