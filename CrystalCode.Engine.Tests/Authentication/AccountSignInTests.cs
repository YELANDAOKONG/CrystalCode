using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Authentication;

public sealed class AccountSignInTests
{
    [Fact]
    public void TryFind_NeverPicksAProviderWhenNoNameIsGiven()
    {
        var catalog = new ProviderCatalog([FakeIntegration.AccountProvider()]);

        Assert.False(AccountSignIn.TryFind(catalog, string.Empty, out _, out var error));
        Assert.Contains("Name an account provider: account", error, StringComparison.Ordinal);
        Assert.True(AccountSignIn.TryFind(catalog, "ACCOUNT", out var provider, out _));
        Assert.Equal("account", provider.Name.Value);
    }

    [Fact]
    public void TryFind_ExplainsEveryFailure()
    {
        var empty = new ProviderCatalog([]);
        var two = new ProviderCatalog([FakeIntegration.AccountProvider("one"), FakeIntegration.AccountProvider("two")]);
        var keyOnly = new ProviderCatalog([new ProviderDefinition(
            ProviderName.Parse("plain"), ProviderProtocol.OpenAI, new Uri("https://example.test/"),
            new Dictionary<string, ModelSettings> { ["m"] = new(1000) })]);

        Assert.False(AccountSignIn.TryFind(empty, "x", out _, out var none));
        Assert.Contains("providers.json", none, StringComparison.Ordinal);
        Assert.False(AccountSignIn.TryFind(two, string.Empty, out _, out var ambiguous));
        Assert.Contains("one, two", ambiguous, StringComparison.Ordinal);
        Assert.False(AccountSignIn.TryFind(two, "plain", out _, out var unknown));
        Assert.Contains("not an account provider", unknown, StringComparison.Ordinal);
        Assert.False(AccountSignIn.TryFind(keyOnly, "plain", out _, out var noAccounts));
        Assert.Contains("No account providers", noAccounts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_RevokesThenDeletesAndReportsAWarning()
    {
        using var home = new TemporaryHome();
        var integration = new FakeIntegration { RevokeWarning = "not confirmed" };
        var registry = new PluginRegistry();
        registry.Add(integration);
        var credentials = new CredentialStore(home.Home);
        var provider = FakeIntegration.AccountProvider();
        var signIn = new AccountSignIn(credentials, registry);
        await credentials.Authentication.Open(provider).WriteAsync(FakeIntegration.Marker, CancellationToken.None);

        var result = await signIn.LogoutAsync(provider, CancellationToken.None);

        Assert.True(result.Removed);
        Assert.Equal("not confirmed", result.Warning);
        Assert.Equal(1, integration.Revoked);
        Assert.False(credentials.Authentication.IsSignedIn(provider));
        Assert.False((await signIn.LogoutAsync(provider, CancellationToken.None)).Removed);
    }

    [Fact]
    public async Task Login_RefusesAnIntegrationThatRejectsTheProvider()
    {
        using var home = new TemporaryHome();
        var integration = new FakeIntegration { Invalid = "the address is wrong." };
        var registry = new PluginRegistry();
        registry.Add(integration);
        var signIn = new AccountSignIn(new CredentialStore(home.Home), registry);

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => signIn.LoginAsync(FakeIntegration.AccountProvider(), new NoInteraction(), CancellationToken.None));

        Assert.Contains("the address is wrong", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SlashLogin_WithoutABrowserIsRefusedAndPointsToTheCommand()
    {
        using var session = AccountSession(createLogin: null, out _);
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/login", CancellationToken.None);

        Assert.Contains(
            session.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains("crystal login", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SlashLogin_RunsTheIntegrationThroughTheFrontEnd()
    {
        var login = new ScriptedLogin();
        using var session = AccountSession((_, _) => login, out var credentials);
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/login account", CancellationToken.None);

        Assert.Equal(1, login.Opened);
        Assert.Contains("Opening the sign-in page.", login.Shown);
        Assert.Contains(
            session.Observer.Events.OfType<NoteWritten>(),
            note => note.Text == "Signed in to account.");
        Assert.True(credentials().Authentication.IsSignedIn(FakeIntegration.AccountProvider()));

        await session.Session.SubmitAsync("/logout account", CancellationToken.None);

        Assert.False(credentials().Authentication.IsSignedIn(FakeIntegration.AccountProvider()));
        Assert.Contains(
            session.Observer.Events.OfType<NoteWritten>(),
            note => note.Text.StartsWith("Signed out of account.", StringComparison.Ordinal));
    }

    private static HeadlessSession AccountSession(
        Func<string, bool, ILoginInteraction>? createLogin,
        out Func<CredentialStore> credentials)
    {
        var settings = HeadlessSession.ScriptedSettings();
        var withAccount = new HarnessSettings(
            settings.Provider,
            settings.Model,
            settings.Approval,
            0.8,
            settings.Catalog.Overlay([FakeIntegration.AccountProvider()]));
        var session = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: withAccount,
            createLogin: createLogin,
            configurePlugins: registry => registry.Add(new FakeIntegration()));
        credentials = () => new CredentialStore(session.Home);
        return session;
    }

    private sealed class NoInteraction : ILoginInteraction
    {
        public Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShowAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);

        public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
