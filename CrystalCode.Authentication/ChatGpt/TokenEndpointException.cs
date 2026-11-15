namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// The authorization server refused a token request. Carries only the HTTP
/// status and the OAuth error code, never a token or a request body.
/// </summary>
internal sealed class TokenEndpointException : Exception
{
    public TokenEndpointException(int statusCode, string? errorCode)
        : base($"The token endpoint answered HTTP {statusCode}"
            + (string.IsNullOrEmpty(errorCode) ? "." : $" ({errorCode})."))
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }

    public string? ErrorCode { get; }

    public bool IsInvalidGrant => string.Equals(ErrorCode, "invalid_grant", StringComparison.Ordinal);
}
