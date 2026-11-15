using System.Text.Json;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// Calls the token endpoint for the authorization-code and refresh grants.
/// </summary>
internal sealed class ChatGptTokenClient
{
    private readonly HttpClient _http;

    public ChatGptTokenClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    public Task<ChatGptTokenResponse> ExchangeCodeAsync(
        string clientId,
        string code,
        string verifier,
        string redirectUri,
        CancellationToken cancellationToken) =>
        PostAsync(
            [
                ("grant_type", "authorization_code"),
                ("client_id", clientId),
                ("code", code),
                ("code_verifier", verifier),
                ("redirect_uri", redirectUri),
                ("resource", ChatGptEndpoints.Resource)
            ],
            cancellationToken);

    public Task<ChatGptTokenResponse> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken) =>
        // Scope is omitted so the refresh keeps the original grant.
        PostAsync(
            [
                ("grant_type", "refresh_token"),
                ("client_id", clientId),
                ("refresh_token", refreshToken),
                ("resource", ChatGptEndpoints.Resource)
            ],
            cancellationToken);

    /// <summary>
    /// Ends the renewable session at the service. Returns false when the
    /// service did not answer with success.
    /// </summary>
    public async Task<bool> RevokeAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var discovery = await _http.GetStringAsync(ChatGptEndpoints.DiscoveryUrl, cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(discovery);
        var endpoint = ReadString(document.RootElement, "revocation_endpoint");
        if (endpoint is null || !endpoint.StartsWith(ChatGptEndpoints.Issuer + "/", StringComparison.Ordinal))
        {
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("token", refreshToken),
                new KeyValuePair<string, string>("token_type_hint", "refresh_token"),
                new KeyValuePair<string, string>("client_id", clientId)
            ])
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    private async Task<ChatGptTokenResponse> PostAsync(
        IEnumerable<(string Name, string Value)> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ChatGptEndpoints.TokenUrl)
        {
            Content = new FormUrlEncodedContent(
                form.Select(static pair => new KeyValuePair<string, string>(pair.Name, pair.Value)))
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new TokenEndpointException((int)response.StatusCode, ReadErrorCode(body));
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var scope = ReadString(root, "scope") ?? string.Empty;
            return new ChatGptTokenResponse(
                ReadString(root, "access_token") ?? throw Invalid(),
                ReadString(root, "refresh_token") ?? throw Invalid(),
                ReadString(root, "id_token"),
                root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
                    ? seconds
                    : throw Invalid(),
                scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (JsonException)
        {
            throw new TokenEndpointException((int)response.StatusCode, "invalid_response");
        }
    }

    private static TokenEndpointException Invalid() => new(200, "invalid_response");

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static string? ReadErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return ReadString(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
