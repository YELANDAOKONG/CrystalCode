using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Authentication;

/// <summary>
/// Slash completions and the status text for <c>/login</c> and
/// <c>/logout</c>. Login state is read when the menu is built, and the
/// session rebuilds the menu after every sign-in and sign-out.
/// </summary>
public static class AccountCompletions
{
    private const string SignedIn = "Signed In";
    private const string SignedOut = "Signed Out";

    /// <summary>
    /// Every account provider, each with the <c>--no-browser</c> option.
    /// </summary>
    public static IReadOnlyList<SlashCompletion> ForLogin(ProviderCatalog catalog, CredentialStore credentials) =>
    [
        .. Names(catalog).Select(account => new SlashCompletion(
            account.Name.Value,
            State(credentials, account),
            [account.Name.Value],
            [new SlashCompletion(
                AccountCommandArguments.NoBrowserOption,
                "Show the link instead of opening a browser",
                [AccountCommandArguments.NoBrowserOption])]))
    ];

    /// <summary>
    /// Only the account providers that have a saved login.
    /// </summary>
    public static IReadOnlyList<SlashCompletion> ForLogout(ProviderCatalog catalog, CredentialStore credentials) =>
    [
        .. Names(catalog)
            .Where(account => IsSignedIn(credentials, account))
            .Select(account => new SlashCompletion(account.Name.Value, SignedIn, [account.Name.Value]))
    ];

    /// <summary>
    /// The text a bare <c>/login</c> or <c>/logout</c> shows.
    /// </summary>
    public static string Describe(ProviderCatalog catalog, CredentialStore credentials, string verb)
    {
        var accounts = Names(catalog);
        if (accounts.Count == 0)
        {
            return "No account providers are configured. Add one with \"type\": \"authentication\" to providers.json.";
        }

        var width = accounts.Max(account => account.Name.Value.Length);
        var lines = accounts.Select(account => $"  {account.Name.Value.PadRight(width)}  {State(credentials, account)}");
        return "Account providers\n" + string.Join('\n', lines) + $"\nRun /{verb} <provider>.";
    }

    private static IReadOnlyList<ProviderDefinition> Names(ProviderCatalog catalog) =>
        [.. AccountSignIn.List(catalog).DistinctBy(static account => account.Name.Value)];

    private static string State(CredentialStore credentials, ProviderDefinition account) =>
        IsSignedIn(credentials, account) ? SignedIn : SignedOut;

    // A damaged login file must not break the whole command menu; it reads as signed out here
    // and the sign-in itself reports the problem.
    private static bool IsSignedIn(CredentialStore credentials, ProviderDefinition account)
    {
        try
        {
            return credentials.Authentication.IsSignedIn(account);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
