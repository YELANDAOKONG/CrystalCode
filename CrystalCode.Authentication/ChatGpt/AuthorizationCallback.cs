using System.Web;

using CrystalCode.Engine.Authentication;

namespace CrystalCode.Authentication.ChatGpt;

/// <summary>
/// The query values of the address the browser came back to.
/// </summary>
internal sealed record AuthorizationCallback(
    string? Code,
    string? State,
    string? Error,
    string? ClientId)
{
    public static AuthorizationCallback Parse(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.AbsolutePath, ChatGptEndpoints.CallbackPath, StringComparison.Ordinal))
        {
            throw new AuthenticationFailedException(
                "That is not the address the browser was sent back to.");
        }

        var query = HttpUtility.ParseQueryString(uri.Query);
        return new AuthorizationCallback(
            query["code"],
            query["state"],
            query["error"],
            query["client_id"]);
    }

    // The code is a secret.
    public override string ToString() => nameof(AuthorizationCallback);
}
