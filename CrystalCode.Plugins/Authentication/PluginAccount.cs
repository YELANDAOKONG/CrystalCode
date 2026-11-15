namespace CrystalCode.Plugins.Authentication;

/// <summary>
/// The provider entry an authentication integration serves. It carries the
/// facts the integration needs to check the entry and no credential.
/// </summary>
public sealed record PluginAccount
{
    public PluginAccount(string provider, string protocol, Uri baseUri, IEnumerable<string> models)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(models);
        Provider = provider.Trim();
        Protocol = protocol.Trim();
        BaseUri = baseUri;
        Models = [.. models];
    }

    /// <summary>Gets the provider name the login is saved under.</summary>
    public string Provider { get; }

    /// <summary>Gets the inference protocol token of the entry.</summary>
    public string Protocol { get; }

    /// <summary>
    /// Gets the address requests go to. A token must be sent only to an
    /// address the integration recognizes as the resource it was issued for.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>Gets the model names the entry declares.</summary>
    public IReadOnlyList<string> Models { get; }

    public override string ToString() => nameof(PluginAccount);
}
