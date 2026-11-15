using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Authentication;

public sealed class AccountCommandTests
{
    [Fact]
    public void Parse_ReadsAProviderAndTheNoBrowserOptionInAnyOrder()
    {
        var first = AccountCommandArguments.Parse("account --no-browser", allowNoBrowser: true);
        var second = AccountCommandArguments.Parse("--NO-BROWSER account", allowNoBrowser: true);

        Assert.Equal(("account", true, null), (first.Provider, first.NoBrowser, first.Error));
        Assert.Equal(("account", true, null), (second.Provider, second.NoBrowser, second.Error));
        Assert.Equal(string.Empty, AccountCommandArguments.Parse("  ", allowNoBrowser: true).Provider);
    }

    [Theory]
    [InlineData("one two", true, "Name one account provider.")]
    [InlineData("--wat", true, "Unknown option --wat.")]
    [InlineData("account --no-browser", false, "--no-browser applies only to /login.")]
    public void Parse_ExplainsBadInput(string text, bool allow, string message) =>
        Assert.Equal(message, AccountCommandArguments.Parse(text, allow).Error);

    [Fact]
    public async Task Completions_LoginListsEveryAccountAndLogoutOnlySignedInOnes()
    {
        using var home = new TemporaryHome();
        var credentials = new CredentialStore(home.Home);
        var catalog = new ProviderCatalog([FakeIntegration.AccountProvider("one"), FakeIntegration.AccountProvider("two")]);
        await credentials.Authentication.Open(FakeIntegration.AccountProvider("two"))
            .WriteAsync(FakeIntegration.Marker, CancellationToken.None);

        var login = AccountCompletions.ForLogin(catalog, credentials);
        var logout = AccountCompletions.ForLogout(catalog, credentials);

        Assert.Equal(["one", "two"], login.Select(item => item.Name));
        Assert.Equal(["Signed Out", "Signed In"], login.Select(item => item.Help));
        Assert.All(login, item => Assert.Equal("--no-browser", Assert.Single(item.ArgumentOptions).Name));
        Assert.Equal("two", Assert.Single(logout).Name);
        Assert.Empty(Assert.Single(logout).ArgumentOptions);
    }

    [Fact]
    public void Completions_ReadADamagedLoginFileAsSignedOut()
    {
        using var home = new TemporaryHome();
        home.Home.EnsureCreated();
        File.WriteAllText(home.Home.AuthenticationCredentialsPath, "{ not json");
        var catalog = new ProviderCatalog([FakeIntegration.AccountProvider("one")]);

        var login = AccountCompletions.ForLogin(catalog, new CredentialStore(home.Home));

        Assert.Equal("Signed Out", Assert.Single(login).Help);
    }

    [Fact]
    public void Document_IsATableOfStateOrExplainsHowToAddAProvider()
    {
        using var home = new TemporaryHome();
        var credentials = new CredentialStore(home.Home);

        var document = AccountCompletions.Document(
            new ProviderCatalog([FakeIntegration.AccountProvider("one")]), credentials, "login");
        var empty = AccountCompletions.Document(new ProviderCatalog([]), credentials, "login");

        Assert.Equal("Account providers  1", document.Title);
        var table = document.Blocks[0];
        Assert.Equal(["Provider", "Integration", "State"], table.Columns);
        Assert.Equal(["one", "fake", "Signed Out"], Assert.Single(table.Rows));
        Assert.Equal("Run /login <provider>.", Assert.Single(document.Blocks[1].Lines));
        Assert.Contains("providers.json", Assert.Single(Assert.Single(empty.Blocks).Lines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareLoginAndLogout_ShowACardWithoutSigningInOrOut()
    {
        using var session = AccountSession(out _);
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/login", CancellationToken.None);
        await session.Session.SubmitAsync("/logout", CancellationToken.None);

        var cards = session.Observer.Events.OfType<DocumentWritten>().Select(card => card.Document).ToArray();
        Assert.Equal(2, cards.Length);
        Assert.All(cards, card => Assert.Equal("Account providers  1", card.Title));
        Assert.Equal("Run /logout <provider>.", cards[1].Blocks[1].Lines[0]);
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
    }

    [Fact]
    public async Task TheMenu_RefreshesLoginStateAfterSignInAndSignOut()
    {
        using var session = AccountSession(out var credentials, createLogin: (_, _) => new ScriptedLogin());
        await session.Session.StartAsync(CancellationToken.None);
        Assert.Equal("Signed Out", LatestHelp(session, "login"));
        Assert.Empty(LatestArguments(session, "logout"));

        await session.Session.SubmitAsync("/login account", CancellationToken.None);

        Assert.Equal("Signed In", LatestHelp(session, "login"));
        Assert.Equal("account", Assert.Single(LatestArguments(session, "logout")).Name);

        await session.Session.SubmitAsync("/logout account", CancellationToken.None);

        Assert.Equal("Signed Out", LatestHelp(session, "login"));
        Assert.Empty(LatestArguments(session, "logout"));
        Assert.False(credentials().Authentication.IsSignedIn(FakeIntegration.AccountProvider()));
    }

    [Fact]
    public async Task NoBrowser_IsPassedToTheFrontEndAndAFailedOpenReportsIt()
    {
        string? name = null;
        bool? openBrowser = null;
        var login = new ScriptedLogin(opensBrowser: false);
        using var session = AccountSession(
            out _,
            createLogin: (provider, open) =>
            {
                name = provider;
                openBrowser = open;
                return login;
            });
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/login account --no-browser", CancellationToken.None);

        Assert.Equal(("account", false), (name, openBrowser));
        Assert.Contains(
            session.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text == "The page could not be opened.");
    }

    [Fact]
    public async Task Esc_CancelsTheSignInWithANoteAndTheNextLoginRuns()
    {
        var attempts = 0;
        using var session = AccountSession(
            out var credentials,
            createLogin: (_, _) => ++attempts == 1 ? new CancellingLogin() : new ScriptedLogin());
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/login account", CancellationToken.None);

        Assert.Contains(
            session.Observer.Events.OfType<NoteWritten>(),
            note => note.Text == "Signing in was cancelled.");
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
        Assert.False(credentials().Authentication.IsSignedIn(FakeIntegration.AccountProvider()));

        await session.Session.SubmitAsync("/login account", CancellationToken.None);

        Assert.True(credentials().Authentication.IsSignedIn(FakeIntegration.AccountProvider()));
    }

    [Fact]
    public async Task TryInterrupt_CancelsAWaitingSignInAndFreesTheNextOne()
    {
        var waiting = new WaitingLogin();
        var attempts = 0;
        using var session = AccountSession(
            out var credentials,
            createLogin: (_, _) => ++attempts == 1 ? waiting : new ScriptedLogin());
        await session.Session.StartAsync(CancellationToken.None);

        var first = session.Session.SubmitAsync("/login account", CancellationToken.None);
        await waiting.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(session.Session.TryInterrupt());
        await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(
            session.Observer.Events.OfType<NoteWritten>(),
            note => note.Text == "Signing in was cancelled.");
        Assert.False(session.Session.TryInterrupt());

        await session.Session.SubmitAsync("/login account", CancellationToken.None);
        Assert.True(credentials().Authentication.IsSignedIn(FakeIntegration.AccountProvider()));
    }

    [Fact]
    public async Task Login_RejectsABadOption()
    {
        using var session = AccountSession(out _);
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/logout account --no-browser", CancellationToken.None);

        Assert.Contains(
            session.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text == "--no-browser applies only to /login.");
    }

    private static string LatestHelp(HeadlessSession session, string name) =>
        session.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands.Single(item => item.Name == name)
            .ArgumentOptions.Select(item => item.Help).FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<SlashCompletion> LatestArguments(HeadlessSession session, string name) =>
        session.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands.Single(item => item.Name == name)
            .ArgumentOptions;

    private static HeadlessSession AccountSession(
        out Func<CredentialStore> credentials,
        Func<string, bool, ILoginInteraction>? createLogin = null)
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
            createLogin: createLogin ?? ((_, _) => new ScriptedLogin()),
            configurePlugins: registry => registry.Add(new FakeIntegration()));
        credentials = () => new CredentialStore(session.Home);
        return session;
    }

    private sealed class CancellingLogin : ILoginInteraction
    {
        public Task ShowAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            throw new OperationCanceledException();

        // The fake integration opens the page first, so Esc is simulated there.
        public Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken) =>
            throw new OperationCanceledException();
    }

    private sealed class WaitingLogin : ILoginInteraction
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ShowAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string> ReadAsync(string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);

        public async Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return true;
        }
    }
}
