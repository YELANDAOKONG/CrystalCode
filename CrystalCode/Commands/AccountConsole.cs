using System.Text.Json;

using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Home.Settings;
using CrystalCode.Engine.Plugins;
using CrystalCode.Terminal;

namespace CrystalCode.Commands;

/// <summary>
/// Runs <c>crystal login</c> and <c>crystal logout</c>. Output is plain lines:
/// the sign-in prints a link and may read a pasted address, so it is not a
/// table.
/// </summary>
public static class AccountConsole
{
    public static async Task<int> LoginAsync(
        AccountSettings settings,
        TextWriter output,
        TextReader input,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!TryOpen(settings, error, out var signIn, out var provider))
        {
            return 1;
        }

        try
        {
            await signIn.LoginAsync(provider, new ConsoleLoginInteraction(output, input), cancellationToken);
            return 0;
        }
        catch (AuthenticationFailedException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("Signing in was cancelled.");
            return 1;
        }
    }

    public static async Task<int> LogoutAsync(
        AccountSettings settings,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!TryOpen(settings, error, out var signIn, out var provider))
        {
            return 1;
        }

        var result = await signIn.LogoutAsync(provider, cancellationToken);
        await output.WriteLineAsync(result.Removed
            ? $"Signed out of {provider.Name.Value}."
            : $"{provider.Name.Value} had no saved login.");
        if (result.Warning is not null)
        {
            await output.WriteLineAsync(result.Warning);
        }

        return 0;
    }

    private static bool TryOpen(
        AccountSettings settings,
        TextWriter error,
        out AccountSignIn signIn,
        out Engine.Configuration.ProviderDefinition provider)
    {
        signIn = null!;
        provider = null!;
        try
        {
            var home = CrystalHome.Resolve(settings.Home);
            var loaded = new DiskSettingsStore(home).LoadOrCreate();
            if (!AccountSignIn.TryFind(loaded.Catalog, settings.Provider ?? string.Empty, out provider, out var problem))
            {
                error.WriteLine(problem);
                return false;
            }

            signIn = new AccountSignIn(new CredentialStore(home), PluginRegistry.CreateBuiltIn());
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            error.WriteLine("The configuration could not be read: " + exception.Message);
            return false;
        }
    }
}
