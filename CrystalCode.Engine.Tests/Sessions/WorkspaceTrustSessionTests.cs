using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class WorkspaceTrustSessionTests
{
    [Fact]
    public async Task ChangeDirectory_DeclineStaysAndDoesNotRecordTrust()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, current.Path, prompt);

        var quit = await opened.Session.SubmitAsync("/cd " + other.Path, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(1, prompt.Asked);
        Assert.Contains(Notes(opened.Observer), text => text == "Staying in " + new Workspace(current.Path).Root);
        Assert.False(File.Exists(home.Home.TrustedPath));
        opened.Session.Close();
    }

    [Fact]
    public async Task ChangeDirectory_AcceptRecordsTheGitRoot()
    {
        using var home = new TemporaryHome();
        using var repo = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(repo.Path, ".git"));
        var first = Path.Combine(repo.Path, "first");
        var second = Path.Combine(repo.Path, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, first, prompt);

        var quit = await opened.Session.SubmitAsync("/cd " + second, CancellationToken.None);
        var again = await opened.Session.SubmitAsync("/cd " + first, CancellationToken.None);

        Assert.False(quit);
        Assert.False(again);
        Assert.Equal(1, prompt.Asked);
        Assert.NotNull(prompt.Last);
        Assert.True(prompt.Last.CoversRepository);
        Assert.Equal(GitRoot.TrustRoot(repo.Path), prompt.Last.TrustRoot);
        Assert.Contains(
            GitRoot.TrustRoot(repo.Path),
            File.ReadAllText(home.Home.TrustedPath),
            StringComparison.Ordinal);
        Assert.Contains(Notes(opened.Observer), text => text.StartsWith("Workspace  ", StringComparison.Ordinal));
        opened.Session.Close();
    }

    [Fact]
    public async Task ChangeDirectory_WhenTrustIsOffDoesNotAsk()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, current.Path, prompt, workspaceTrust: false);

        var quit = await opened.Session.SubmitAsync("/cd " + other.Path, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(0, prompt.Asked);
        Assert.Contains(
            Notes(opened.Observer),
            text => text == "Workspace  " + new Workspace(other.Path).Root);
        Assert.False(File.Exists(home.Home.TrustedPath));
        opened.Session.Close();
    }

    [Fact]
    public async Task TrustOn_DeclineExitsWithoutAGrant()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, current.Path, prompt);

        var quit = await opened.Session.SubmitAsync("/trust on", CancellationToken.None);

        Assert.True(quit);
        Assert.Equal(1, prompt.Asked);
        Assert.False(File.Exists(home.Home.TrustedPath));
        Assert.DoesNotContain(
            "workspaceTrust",
            File.ReadAllText(home.Home.ConfigPath),
            StringComparison.Ordinal);
        opened.Session.Close();
    }

    [Fact]
    public async Task ChangeDirectory_OperatorSpaceDoesNotAskOrRecordTheParent()
    {
        using var home = new TemporaryHome();
        Directory.CreateDirectory(Path.Combine(home.Root, ".git"));
        var space = OperatorSpace.EnsureCreated(home.Home);
        using var current = new TemporaryWorkspace();
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, current.Path, prompt);

        var quit = await opened.Session.SubmitAsync("/cd " + space, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(0, prompt.Asked);
        Assert.Contains(
            Notes(opened.Observer),
            text => text == "Workspace  " + space);
        Assert.False(File.Exists(home.Home.TrustedPath));
        opened.Session.Close();
    }

    [Fact]
    public async Task ChangeDirectory_ChildOfOperatorSpaceStillAsks()
    {
        using var home = new TemporaryHome();
        var space = OperatorSpace.EnsureCreated(home.Home);
        var child = Path.Combine(space, "notes");
        Directory.CreateDirectory(child);
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, space, prompt);

        var quit = await opened.Session.SubmitAsync("/cd " + child, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(1, prompt.Asked);
        Assert.Contains(
            Notes(opened.Observer),
            text => text == "Staying in " + new Workspace(space).Root);
        opened.Session.Close();
    }

    [Fact]
    public async Task TrustForget_OperatorSpaceStaysTrusted()
    {
        using var home = new TemporaryHome();
        Directory.CreateDirectory(Path.Combine(home.Root, ".git"));
        var space = OperatorSpace.EnsureCreated(home.Home);
        var store = new WorkspaceTrustStore(home.Home);
        store.Remember(home.Root);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, space, prompt);

        var shown = await opened.Session.SubmitAsync("/trust", CancellationToken.None);
        var forgotten = await opened.Session.SubmitAsync("/trust forget", CancellationToken.None);

        Assert.False(shown);
        Assert.False(forgotten);
        Assert.Equal(0, prompt.Asked);
        Assert.Contains(Notes(opened.Observer), text => text == "Trust root  " + space);
        Assert.Contains(Notes(opened.Observer), text => text == "Trusted  yes");
        Assert.Contains(Notes(opened.Observer), text => text == "The operator space stays trusted.");
        Assert.True(store.Contains(home.Root));
        Assert.DoesNotContain(space, File.ReadAllText(home.Home.TrustedPath), StringComparison.Ordinal);
        opened.Session.Close();
    }

    [Fact]
    public async Task TrustForget_DropsTheCurrentRoot()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        new WorkspaceTrustStore(home.Home).Remember(current.Path);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, current.Path, prompt);

        var quit = await opened.Session.SubmitAsync("/trust forget", CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(0, prompt.Asked);
        Assert.False(new WorkspaceTrustStore(home.Home).Contains(current.Path));
        Assert.Contains(Notes(opened.Observer), text => text.StartsWith("Forgot  ", StringComparison.Ordinal));
        opened.Session.Close();
    }

    private static IEnumerable<string> Notes(RecordingSessionObserver observer) =>
        observer.Events.OfType<NoteWritten>().Select(note => note.Text);

    private static OpenedSession Open(
        TemporaryHome home,
        string workspace,
        IWorkspaceTrustPrompt trust,
        bool workspaceTrust = true)
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(200_000) });
        var settings = new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]),
            workspaceTrust: workspaceTrust);
        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(new ScriptedChatPlugin(new ScriptedStreamingClient()));
        var observer = new RecordingSessionObserver();
        var session = CodingSession.Create(
            settings,
            new SettingsStore(home.Home),
            new CredentialStore(home.Home),
            home.Home,
            workspace,
            new SessionFrontEnd(
                observer,
                new RecordingApprovalPrompt(ApprovalChoice.Deny),
                new FixedUserPrompt("unused"),
                new DecliningSessionChooser(),
                trust),
            plugins);
        return new OpenedSession(session, observer);
    }

    private sealed record OpenedSession(CodingSession Session, RecordingSessionObserver Observer);
}
