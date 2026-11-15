namespace CrystalCode.Plugins.Authentication;

/// <summary>
/// A sign-in or token renewal failed in a way the operator can act on. The
/// host shows the message as written, so it must be plain English and must
/// not contain a token or an authorization code. From <c>Validate</c>,
/// <c>LoginAsync</c>, and <c>RevokeAsync</c> any other exception is shown with
/// the plugin name and its type, followed by its message, so keep tokens out
/// of every message.
/// </summary>
public sealed class PluginAuthenticationException : Exception
{
    public PluginAuthenticationException(string message)
        : base(message)
    {
    }

    public PluginAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
