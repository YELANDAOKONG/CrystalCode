namespace CrystalCode.Engine.Authentication;

/// <summary>
/// A sign-in or token refresh failed. The message is plain English and never
/// contains a token or an authorization code.
/// </summary>
public sealed class AuthenticationFailedException : Exception
{
    public AuthenticationFailedException(string message)
        : base(message)
    {
    }

    public AuthenticationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
