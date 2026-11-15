using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Authentication.Interfaces;

/// <summary>
/// An account login flow for providers whose type is authentication. The
/// engine knows an integration only by its registered id. Everything vendor
/// specific, including the sign-in steps and token refresh, lives in the
/// implementation.
/// </summary>
public interface IAuthenticationIntegration
{
    /// <summary>
    /// Gets the id a provider entry names in its <c>integration</c> field.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Checks a provider entry, including whether tokens may be sent to its
    /// base URI. Returns null when it is valid, or a plain-English reason.
    /// </summary>
    string? Validate(ProviderDefinition provider);

    /// <summary>
    /// Runs the sign-in flow and saves the result through the vault. Throws
    /// <see cref="AuthenticationFailedException"/> when the flow fails. The
    /// message must not contain a token.
    /// </summary>
    Task LoginAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        ILoginInteraction interaction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the handler that attaches a valid credential to each request.
    /// The caller sets its inner handler.
    /// </summary>
    DelegatingHandler CreateHandler(ProviderDefinition provider, IAuthenticationVault vault);

    /// <summary>
    /// Ends the renewable session at the service before the engine deletes the
    /// saved login. Returns a plain-English warning when the service could not
    /// confirm it, or null. The default has nothing to end.
    /// </summary>
    Task<string?> RevokeAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
