using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Plugins.Authentication;
using CrystalCode.Plugins.Authentication.Interfaces;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Serves a plugin's account login to the engine. The plugin sees facts and a
/// vault slot, never an engine type. What a check, a sign-in, or a revoke
/// throws is shown with the plugin name, so a plugin must keep tokens out of
/// its exception messages.
/// </summary>
internal sealed class DiskAuthenticationIntegration : IAuthenticationIntegration
{
    private readonly string _plugin;
    private readonly IPluginAuthentication _authentication;

    public DiskAuthenticationIntegration(string plugin, IPluginAuthentication authentication)
    {
        _plugin = plugin;
        _authentication = authentication;
    }

    public string Id => _authentication.Id;

    public string? Validate(ProviderDefinition provider)
    {
        try
        {
            return _authentication.Validate(Account(provider));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed("check the provider", exception);
        }
    }

    public async Task LoginAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        ILoginInteraction interaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await _authentication.LoginAsync(
                Account(provider),
                new DiskVault(vault),
                new DiskLogin(interaction),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not AuthenticationFailedException)
        {
            throw new AuthenticationFailedException(Failed("sign in", exception));
        }
    }

    // The plugin's handler is used as is: it sits in the client's own request path.
    public DelegatingHandler CreateHandler(ProviderDefinition provider, IAuthenticationVault vault) =>
        _authentication.CreateHandler(Account(provider), new DiskVault(vault));

    public async Task<string?> RevokeAsync(
        ProviderDefinition provider,
        IAuthenticationVault vault,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _authentication.RevokeAsync(Account(provider), new DiskVault(vault), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed("end the session", exception);
        }
    }

    private static PluginAccount Account(ProviderDefinition provider) =>
        new(provider.Name.Value, provider.Protocol.Value, provider.BaseUri, provider.Models.Keys);

    // A plugin is code the operator installed, and the message is what they need to fix it.
    private string Failed(string action, Exception exception) =>
        exception is PluginAuthenticationException || string.IsNullOrWhiteSpace(exception.Message)
            ? exception is PluginAuthenticationException
                ? exception.Message
                : $"Plugin sign-in '{_plugin}' could not {action} ({exception.GetType().Name})."
            : $"Plugin sign-in '{_plugin}' could not {action} ({exception.GetType().Name}): {exception.Message}";
}
