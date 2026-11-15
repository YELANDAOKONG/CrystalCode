namespace CrystalCode.Engine.Authentication;

/// <summary>
/// What a client factory needs to reach one provider: an API key, or for an
/// authentication provider an integration handler and no key.
/// </summary>
public sealed record ProviderCredential
{
    public ProviderCredential(string apiKey, DelegatingHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        ApiKey = apiKey;
        Handler = handler;
    }

    public string ApiKey { get; }

    /// <summary>
    /// Gets the integration handler, or null for an API key provider.
    /// </summary>
    public DelegatingHandler? Handler { get; }

    /// <summary>
    /// Builds an HTTP client around the handler. The client owns the handler.
    /// </summary>
    public HttpClient CreateHttpClient(TimeSpan timeout)
    {
        if (Handler is null)
        {
            throw new InvalidOperationException("This credential has no handler.");
        }

        Handler.InnerHandler ??= new SocketsHttpHandler();
        return new HttpClient(Handler, disposeHandler: true) { Timeout = timeout };
    }

    // The key is a secret.
    public override string ToString() => nameof(ProviderCredential);
}
