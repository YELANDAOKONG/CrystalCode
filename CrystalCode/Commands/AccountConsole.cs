using System.Text.Json;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Home.Settings;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tools;
using CrystalCode.Terminal;

using Spectre.Console;

namespace CrystalCode.Commands;

/// <summary>
/// Runs <c>crystal login</c> and <c>crystal logout</c>. Without a provider
/// name they list account providers and whether each is signed in, as a
/// Spectre table or, with <c>--format text</c>, tab-separated text. Signing
/// in or out prints plain lines in both forms, because the sign-in shows a
/// link the operator may copy and may read a pasted address.
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
        if (!TryOpen(settings, output, error, out var context))
        {
            return 1;
        }

        if (context.List)
        {
            WriteList(context, output);
            return 0;
        }

        try
        {
            await context.SignIn.LoginAsync(
                context.Provider!,
                new ConsoleLoginInteraction(output, input, openBrowser: !settings.NoBrowser, plain: context.Plain),
                cancellationToken);
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
        if (!TryOpen(settings, output, error, out var context))
        {
            return 1;
        }

        if (context.List)
        {
            WriteList(context, output);
            return 0;
        }

        var provider = context.Provider!;
        var result = await context.SignIn.LogoutAsync(provider, cancellationToken);
        await output.WriteLineAsync(result.Removed
            ? $"Signed out of {provider.Name.Value}."
            : $"{provider.Name.Value} had no saved login.");
        if (result.Warning is not null)
        {
            await output.WriteLineAsync(result.Warning);
        }

        return 0;
    }

    private static void WriteList(AccountContext context, TextWriter output)
    {
        if (context.Plain)
        {
            foreach (var account in context.Accounts)
            {
                output.WriteLine(
                    $"{account.Name.Value}\t{account.Integration}\t{State(context, account)}");
            }

            return;
        }

        if (context.Accounts.Count == 0)
        {
            output.WriteLine("No account providers.");
            return;
        }

        var table = new Table { Border = TableBorder.Simple };
        foreach (var column in new[] { "Provider", "Integration", "State" })
        {
            table.AddColumn(new TableColumn($"[{Theme.Chrome}]{column}[/]"));
        }

        foreach (var account in context.Accounts)
        {
            table.AddRow(
                Markup.Escape(account.Name.Value),
                Markup.Escape(account.Integration!),
                Markup.Escape(State(context, account)));
        }

        AnsiConsole.Write(table);
    }

    private static string State(AccountContext context, ProviderDefinition account) =>
        context.Credentials.Authentication.IsSignedIn(account) ? "Signed In" : "Signed Out";

    private static bool TryOpen(
        AccountSettings settings,
        TextWriter output,
        TextWriter error,
        out AccountContext context)
    {
        context = null!;
        if (!ExtensionConsole.TryParseFormat(settings.Format, out var plain, out var formatError))
        {
            error.WriteLine(formatError);
            return false;
        }

        try
        {
            var home = CrystalHome.Resolve(settings.Home);
            var loaded = new DiskSettingsStore(home).LoadOrCreate();
            var catalog = loaded.Catalog;
            var credentials = new CredentialStore(home);
            var registry = PluginRegistry.CreateBuiltIn();
            var accounts = AccountSignIn.List(catalog);
            var name = settings.Provider?.Trim() ?? string.Empty;
            ProviderDefinition? provider = null;
            if (name.Length > 0 && !AccountSignIn.TryFind(catalog, name, out provider, out var problem))
            {
                error.WriteLine(problem);
                return false;
            }

            var find = registry.FindIntegration;
            if (provider?.Integration is { } integration && registry.FindIntegration(integration) is null && loaded.Plugins)
            {
                // Only the operator's own plugins load here: nothing has asked to trust this directory.
                var plugins = PluginCatalog.Load(
                    home,
                    new Workspace(Directory.GetCurrentDirectory()),
                    enabled: true,
                    homeOnly: true);
                find = id => registry.FindIntegration(id) ?? plugins.FindIntegration(id);
                if (find(integration) is null)
                {
                    foreach (var note in plugins.Notes)
                    {
                        error.WriteLine(note);
                    }
                }
            }

            var signIn = new AccountSignIn(credentials, find);
            context = new AccountContext(signIn, credentials, accounts, provider, plain);
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

    private sealed record AccountContext(
        AccountSignIn SignIn,
        CredentialStore Credentials,
        IReadOnlyList<ProviderDefinition> Accounts,
        ProviderDefinition? Provider,
        bool Plain)
    {
        public bool List => Provider is null;
    }
}
