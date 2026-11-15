namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// A successful answer of the token endpoint.
/// </summary>
internal sealed record ChatGptTokenResponse(
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    int ExpiresInSeconds,
    IReadOnlyList<string> Scopes)
{
    // Tokens are secrets.
    public override string ToString() => nameof(ChatGptTokenResponse);
}
