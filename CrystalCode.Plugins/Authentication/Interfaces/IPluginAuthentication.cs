namespace CrystalCode.Plugins.Authentication.Interfaces;

/// <summary>
/// An account login for providers whose <c>type</c> is <c>authentication</c>.
/// A provider entry names it in <c>integration</c>. The host runs the sign-in
/// flow through it, keeps what it saves, and hands the handler it returns to
/// the plugin's own client factory through
/// <see cref="Clients.PluginClientRequest.Handler"/>. A first-party
/// integration with the same <see cref="Id"/> wins.
/// </summary>
public interface IPluginAuthentication
{
    /// <summary>
    /// Gets the token a provider entry puts in <c>integration</c>: a letter,
    /// then letters, digits, <c>_</c>, or <c>-</c>, 1 to 64 characters.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Checks a provider entry, including whether tokens may be sent to its
    /// <see cref="PluginAccount.BaseUri"/>. Returns null when it is valid, or
    /// a plain-English reason.
    /// </summary>
    string? Validate(PluginAccount account);

    /// <summary>
    /// Runs the sign-in flow and saves the result through the vault. Throw
    /// <see cref="PluginAuthenticationException"/> when it fails.
    /// </summary>
    Task LoginAsync(
        PluginAccount account,
        IPluginVault vault,
        IPluginLogin login,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the handler that attaches a valid credential to each request,
    /// renews it when it is about to expire, and retries once after a 401. The
    /// client factory sets the inner handler through
    /// <see cref="Clients.PluginClientRequest.CreateHttpClient"/>.
    /// </summary>
    DelegatingHandler CreateHandler(PluginAccount account, IPluginVault vault);

    /// <summary>
    /// Ends the remote session before the host deletes the saved login.
    /// Returns a plain-English warning when the service could not confirm it,
    /// or null. The default has nothing to end.
    /// </summary>
    Task<string?> RevokeAsync(
        PluginAccount account,
        IPluginVault vault,
        CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
