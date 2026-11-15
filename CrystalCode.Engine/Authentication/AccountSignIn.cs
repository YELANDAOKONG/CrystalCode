using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Engine.Authentication;

/// <summary>
/// Signs an account provider in or out. The terminal commands and the
/// session's <c>/login</c> and <c>/logout</c> share it, so neither copies the
/// provider lookup or the integration checks.
/// </summary>
public sealed class AccountSignIn
{
    private readonly CredentialStore _credentials;
    private readonly Func<string, IAuthenticationIntegration?> _find;

    public AccountSignIn(CredentialStore credentials, PluginRegistry plugins)
        : this(credentials, plugins is null ? throw new ArgumentNullException(nameof(plugins)) : plugins.FindIntegration)
    {
    }

    /// <summary>
    /// Finds integrations through <paramref name="find"/>, so a caller can look
    /// in the registry and then in loaded disk plugins.
    /// </summary>
    public AccountSignIn(CredentialStore credentials, Func<string, IAuthenticationIntegration?> find)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(find);
        _credentials = credentials;
        _find = find;
    }

    /// <summary>
    /// Finds one account provider by name. An empty name is refused.
    /// </summary>
    public static IReadOnlyList<ProviderDefinition> List(ProviderCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return [.. catalog.Providers.Values
            .SelectMany(static variants => variants)
            .Where(static variant => variant.Type == ProviderType.Authentication)];
    }

    public static bool TryFind(
        ProviderCatalog catalog,
        string name,
        out ProviderDefinition provider,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        provider = null!;
        var accounts = List(catalog);
        var wanted = name.Trim();
        if (wanted.Length == 0)
        {
            // Signing in always names its provider, so a bare command never opens a page by itself.
            error = accounts.Count == 0
                ? NoAccountProviders()
                : "Name an account provider: "
                    + string.Join(", ", accounts.Select(static account => account.Name.Value).Distinct())
                    + ".";
            return false;
        }

        var matches = accounts.Where(
            account => string.Equals(account.Name.Value, wanted, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1)
        {
            provider = matches[0];
            error = string.Empty;
            return true;
        }

        error = matches.Length > 1
            ? $"Provider {wanted} lists more than one account endpoint."
            : accounts.Count == 0
                ? NoAccountProviders()
                : $"{wanted} is not an account provider. Account providers: "
                    + string.Join(", ", accounts.Select(static account => account.Name.Value).Distinct()) + ".";
        return false;
    }

    public async Task LoginAsync(
        ProviderDefinition provider,
        ILoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(interaction);
        var integration = Find(provider);
        await integration.LoginAsync(
            provider,
            _credentials.Authentication.Open(provider),
            interaction,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ends the remote session when the integration can, then deletes the
    /// saved login. The result carries a warning when the service did not
    /// confirm the end of the session.
    /// </summary>
    public async Task<LogoutResult> LogoutAsync(
        ProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!_credentials.Authentication.IsSignedIn(provider))
        {
            return new LogoutResult(false, null);
        }

        string? warning = null;
        var integration = _find(provider.Integration!);
        if (integration is not null)
        {
            try
            {
                warning = await integration.RevokeAsync(
                    provider,
                    _credentials.Authentication.Open(provider),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                warning = "The service did not confirm that the session ended.";
            }
        }

        await _credentials.Authentication.RemoveAsync(provider.Name, cancellationToken).ConfigureAwait(false);
        return new LogoutResult(true, warning);
    }

    private IAuthenticationIntegration Find(ProviderDefinition provider)
    {
        var integration = _find(provider.Integration!)
            ?? throw new AuthenticationFailedException(
                $"Provider {provider.Name.Value} needs integration '{provider.Integration}', which is not registered.");
        var invalid = integration.Validate(provider);
        if (invalid is not null)
        {
            throw new AuthenticationFailedException($"Provider {provider.Name.Value}: {invalid}");
        }

        return integration;
    }

    private static string NoAccountProviders() =>
        "No account providers are configured. Add one with \"type\": \"authentication\" to providers.json.";
}
