namespace CrystalCode.Plugins.Clients;

public static class ProviderHttpRequestOptions
{
    /// <summary>Allows the adapter to prepare and count a resend by an authentication handler.</summary>
    public static HttpRequestOptionsKey<Action<HttpRequestMessage>> AdditionalAttempt { get; } =
        new("CrystalCode.Provider.AdditionalHttpAttempt");
}
