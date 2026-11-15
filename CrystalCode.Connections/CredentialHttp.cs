using CrystalCode.Engine.Authentication;

namespace CrystalCode.Connections;

/// <summary>
/// Builds the HTTP client an account login needs. A factory passes the result
/// to its adapter, which then sends every request through the integration's
/// handler; an API key provider gets null and the adapter makes its own client.
/// </summary>
internal static class CredentialHttp
{
    public static HttpClient? Client(ProviderCredential credential, TimeSpan timeout) =>
        credential.Handler is null ? null : credential.CreateHttpClient(timeout);
}
