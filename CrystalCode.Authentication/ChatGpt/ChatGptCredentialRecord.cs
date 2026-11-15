using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// The saved login of one provider: the identity, the issued client, and the
/// rotating tokens. Written to the vault as one JSON object.
/// </summary>
internal sealed record ChatGptCredentialRecord
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string? Email { get; init; }

    public required string Issuer { get; init; }

    public required string Subject { get; init; }

    public required string ClientId { get; init; }

    public string? IdToken { get; init; }

    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public string TokenType { get; init; } = "Bearer";

    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>
    /// Gets when the access token stops working, in UTC.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset SavedAt { get; init; }

    public static ChatGptCredentialRecord Parse(string json) =>
        JsonSerializer.Deserialize<ChatGptCredentialRecord>(json, Options)
        ?? throw new JsonException("The saved login is empty.");

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public bool IsFresh(DateTimeOffset now, TimeSpan skew) => ExpiresAt - now > skew;

    // Tokens are secrets, so nothing but the type name is printed.
    public override string ToString() => nameof(ChatGptCredentialRecord);
}
