using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Authentication.Interfaces;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Sessions;
using CrystalCode.Engine.Tools.Questions;
using CrystalCode.Engine.Tools.Questions.Interfaces;

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
    public void Describe_ListsStateOrExplainsHowToAddAProvider()
    {
        using var home = new TemporaryHome();
        var credentials = new CredentialStore(home.Home);

        var text = AccountCompletions.Describe(
            new ProviderCatalog([FakeIntegration.AccountProvider("one")]), credentials, "login");

        Assert.Equal("Account providers\n  one  Signed Out\nRun /login <provider>.", text);
        Assert.Contains(
            "providers.json",
            AccountCompletions.Describe(new ProviderCatalog([]), credentials, "login"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareLoginAndLogout_ListAccountsWithoutSigningInOrOut()
    {
        using var session = AccountSession(out _);
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/login", CancellationToken.None);
        await session.Session.SubmitAsync("/logout", CancellationToken.None);

        var notes = session.Observer.Events.OfType<NoteWritten>().Select(note => note.Text).ToArray();
        Assert.Contains("Account providers\n  account  Signed Out\nRun /login <provider>.", notes);
        Assert.Contains("Account providers\n  account  Signed Out\nRun /logout <provider>.", notes);
        Assert.Empty(session.Observer.Events.OfType<ErrorWritten>());
    }

    [Fact]
    public async Task TheMenu_RefreshesLoginStateAfterSignInAndSignOut()
    {
        using var session = AccountSession(out var credentials, openLink: (_, _) => Task.FromResult(true));
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
    public async Task NoBrowser_NeverOpensTheLinkAndTheFlowShowsItInstead()
    {
        var opened = 0;
        using var session = AccountSession(
            out _,
            openLink: (_, _) =>
            {
                opened++;
                return Task.FromResult(true);
            });
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/login account --no-browser", CancellationToken.None);

        Assert.Equal(0, opened);
        Assert.Contains(
            session.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text == "The page could not be opened.");
    }

    [Fact]
    public async Task Login_RejectsABadOption()
    {
        using var session = AccountSession(out _, openLink: (_, _) => Task.FromResult(true));
        await session.Session.StartAsync(CancellationToken.None);

        await session.Session.SubmitAsync("/logout account --no-browser", CancellationToken.None);

        Assert.Contains(
            session.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text == "--no-browser applies only to /login.");
    }

    [Fact]
    public async Task Paste_AsksAtOnceWhenNoBrowserWasOpenedAndReasksOnABadAddress()
    {
        var prompt = new ScriptedPrompt("not an address", "https://example.test/auth/callback?code=x");
        var notes = new List<string>();
        var interaction = Interaction(notes, prompt, openBrowser: false);

        Assert.False(await interaction.OpenLinkAsync(new Uri("https://example.test/"), CancellationToken.None));
        var text = await interaction.ReadAsync("Paste it:", CancellationToken.None);

        Assert.Equal("https://example.test/auth/callback?code=x", text);
        Assert.Equal(2, prompt.Asked);
        Assert.Contains(notes, note => note.StartsWith("That is not an address", StringComparison.Ordinal));
        Assert.Equal("Paste it:", prompt.Questions[0].Text);
        Assert.True(prompt.Questions[0].Custom);
        Assert.Empty(prompt.Questions[0].Options);
    }

    [Fact]
    public async Task Paste_WaitsBeforeAskingAfterABrowserOpenedAndEscCancelsTheSignIn()
    {
        var prompt = new ScriptedPrompt(rejected: true);
        var interaction = Interaction([], prompt, openBrowser: true);
        Assert.True(await interaction.OpenLinkAsync(new Uri("https://example.test/"), CancellationToken.None));

        using var early = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => interaction.ReadAsync("Paste it:", early.Token));
        Assert.Equal(0, prompt.Asked);

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => interaction.ReadAsync("Paste it:", CancellationToken.None));
        Assert.Equal("Signing in was cancelled.", error.Message);
        Assert.Equal(1, prompt.Asked);
    }

    private static SessionLoginInteraction Interaction(List<string> notes, IUserPrompt prompt, bool openBrowser) =>
        new(notes.Add, (_, _) => Task.FromResult(true), prompt, openBrowser, pasteDelay: TimeSpan.FromMilliseconds(200));

    private static string LatestHelp(HeadlessSession session, string name) =>
        session.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands.Single(item => item.Name == name)
            .ArgumentOptions.Select(item => item.Help).FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<SlashCompletion> LatestArguments(HeadlessSession session, string name) =>
        session.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands.Single(item => item.Name == name)
            .ArgumentOptions;

    private static HeadlessSession AccountSession(
        out Func<CredentialStore> credentials,
        Func<Uri, CancellationToken, Task<bool>>? openLink = null)
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
            openLink: openLink ?? ((_, _) => Task.FromResult(true)),
            configurePlugins: registry => registry.Add(new FakeIntegration()));
        credentials = () => new CredentialStore(session.Home);
        return session;
    }

    private sealed class ScriptedPrompt : IUserPrompt
    {
        private readonly Queue<string> _answers;
        private readonly bool _rejected;

        public ScriptedPrompt(params string[] answers)
        {
            _answers = new Queue<string>(answers);
        }

        public ScriptedPrompt(bool rejected)
        {
            _answers = new Queue<string>();
            _rejected = rejected;
        }

        public int Asked { get; private set; }

        public List<UserQuestion> Questions { get; } = [];

        public ValueTask<QuestionResponse> AskAsync(
            IReadOnlyList<UserQuestion> questions,
            CancellationToken cancellationToken = default)
        {
            Asked++;
            Questions.Add(questions[0]);
            return ValueTask.FromResult(_rejected || _answers.Count == 0
                ? new QuestionResponse([], IsRejected: true)
                : new QuestionResponse([[_answers.Dequeue()]], IsRejected: false));
        }
    }
}
