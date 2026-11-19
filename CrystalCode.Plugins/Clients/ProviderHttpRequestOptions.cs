namespace CrystalCode.Plugins.Clients;

public static class ProviderHttpRequestOptions
{
    /// <summary>Allows the adapter to prepare a resend by an authentication handler.</summary>
    public static HttpRequestOptionsKey<Action<HttpRequestMessage>> AdditionalAttempt { get; } =
        new("CrystalCode.Provider.AdditionalHttpAttempt");

    /// <summary>Records how many inference sends the authentication handler has started.</summary>
    public static HttpRequestOptionsKey<int> InferenceAttempts { get; } =
        new("CrystalCode.Provider.InferenceAttempts");
}
