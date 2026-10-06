using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ResumeWorkspaceTests
{
    [Fact]
    public async Task ResumePath_DeclineStaysAndDoesNotOpenTheChooser()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var chooser = new ScriptedSessionChooser("unused");
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, current.Path, chooser, prompt);
        var original = opened.Session.SessionId;

        var quit = await opened.Session.SubmitAsync("/resume " + other.Path, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(0, chooser.Calls);
        Assert.Equal(1, prompt.Asked);
        Assert.Equal(original, opened.Session.SessionId);
        Assert.Contains(Notes(opened.Observer), text => text == "Staying in " + Root(current.Path));
        Assert.DoesNotContain(Notes(opened.Observer), text => text.StartsWith("Resumed  ", StringComparison.Ordinal));
        opened.Session.Close();
    }

    [Fact]
    public async Task ResumePath_CancelAfterTrustStaysInTheOriginalWorkspace()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        SaveSession(home, "otherid", other.Path, "from other");
        var chooser = new ScriptedSessionChooser(choice: null);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, current.Path, chooser, prompt);
        var original = opened.Session.SessionId;

        var quit = await opened.Session.SubmitAsync("/resume " + other.Path, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(1, chooser.Calls);
        Assert.False(chooser.ListWorkspace);
        Assert.Equal(original, opened.Session.SessionId);
        Assert.DoesNotContain(Notes(opened.Observer), text => text.StartsWith("Workspace  ", StringComparison.Ordinal));
        Assert.Equal([Root(other.Path)], TrustedDirectories.Read(home.Home.TrustedPath));
        opened.Session.Close();
    }

    [Fact]
    public async Task ResumePath_AcceptEntersThatWorkspace()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var id = SaveSession(home, "otherid", other.Path, "from other");
        var chooser = new ScriptedSessionChooser(id);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, current.Path, chooser, prompt);

        var quit = await opened.Session.SubmitAsync("/resume " + other.Path, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(id, opened.Session.SessionId);
        Assert.Equal(Root(other.Path), LastWorkspace(opened.Observer));
        Assert.Contains(Notes(opened.Observer), text => text == "Workspace  " + Root(other.Path));
        Assert.Contains(Notes(opened.Observer), text => text == "Resumed  " + id);
        opened.Session.Close();
    }

    [Fact]
    public async Task ResumeAll_DeclineDoesNotApplyTheSession()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var id = SaveSession(home, "otherid", other.Path, "from other");
        var chooser = new ScriptedSessionChooser(id);
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, current.Path, chooser, prompt);
        var original = opened.Session.SessionId;

        var quit = await opened.Session.SubmitAsync("/resume all", CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(1, chooser.Calls);
        Assert.True(chooser.ListWorkspace);
        Assert.Contains(chooser.LastSessions, session => session.Id == id);
        Assert.Equal(1, prompt.Asked);
        Assert.Equal(original, opened.Session.SessionId);
        Assert.Contains(Notes(opened.Observer), text => text == "Staying in " + Root(current.Path));
        opened.Session.Close();
    }

    [Fact]
    public async Task ResumeAll_AcceptEntersTheSessionWorkspace()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var id = SaveSession(home, "otherid", other.Path, "from other");
        var chooser = new ScriptedSessionChooser(id);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, current.Path, chooser, prompt);

        var quit = await opened.Session.SubmitAsync("/resume all", CancellationToken.None);

        Assert.False(quit);
        Assert.True(chooser.ListWorkspace);
        Assert.Equal(id, opened.Session.SessionId);
        Assert.Equal(Root(other.Path), LastWorkspace(opened.Observer));
        opened.Session.Close();
    }

    [Fact]
    public async Task ResumeId_FromAnotherWorkspace_StaysHere()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var id = SaveSession(home, "otherid", other.Path, "from other");
        var chooser = new ScriptedSessionChooser(id);
        var prompt = new ScriptedTrustPrompt(accept: false);
        var opened = Open(home, current.Path, chooser, prompt);

        var quit = await opened.Session.SubmitAsync("/resume " + id, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(0, chooser.Calls);
        Assert.Equal(0, prompt.Asked);
        Assert.Equal(id, opened.Session.SessionId);
        Assert.Equal(Root(current.Path), LastWorkspace(opened.Observer));
        Assert.DoesNotContain(Notes(opened.Observer), text => text.StartsWith("Workspace  ", StringComparison.Ordinal));
        opened.Session.Close();
    }

    [Fact]
    public async Task ResumeAll_WhenNoneExist_ReportsNoSessions()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        var chooser = new ScriptedSessionChooser(choice: null);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, current.Path, chooser, prompt);

        var quit = await opened.Session.SubmitAsync("/resume all", CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(0, chooser.Calls);
        Assert.Contains(Errors(opened.Observer), text => text == "No sessions");
        opened.Session.Close();
    }

    [Fact]
    public async Task Resume_AmbiguousTarget_DoesNotChoose()
    {
        using var home = new TemporaryHome();
        using var current = new TemporaryWorkspace();
        const string id = "abcd";
        Directory.CreateDirectory(Path.Combine(current.Path, id));
        SaveSession(home, id, current.Path, "both");
        var chooser = new ScriptedSessionChooser(id);
        var prompt = new ScriptedTrustPrompt(accept: true);
        var opened = Open(home, current.Path, chooser, prompt);

        var quit = await opened.Session.SubmitAsync("/resume " + id, CancellationToken.None);

        Assert.False(quit);
        Assert.Equal(0, chooser.Calls);
        Assert.Contains(
            Errors(opened.Observer),
            text => text == "Resume target matches both a session and a directory.");
        opened.Session.Close();
    }

    private static string SaveSession(TemporaryHome home, string id, string workspace, string text)
    {
        var root = Root(workspace);
        new SessionStore(home.Home).Save(
            new SessionDocument
            {
                Id = id,
                Workspace = root,
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = text }]
            });
        return id;
    }

    private static string Root(string path) => new Workspace(path).Root;

    private static IEnumerable<string> Notes(RecordingSessionObserver observer) =>
        observer.Events.OfType<NoteWritten>().Select(note => note.Text);

    private static IEnumerable<string> Errors(RecordingSessionObserver observer) =>
        observer.Events.OfType<ErrorWritten>().Select(note => note.Text);

    private static string? LastWorkspace(RecordingSessionObserver observer) =>
        observer.Events.OfType<ChromeChanged>().LastOrDefault()?.Chrome.WorkspaceRoot;

    private static OpenedSession Open(
        TemporaryHome home,
        string workspace,
        ISessionChooser chooser,
        IWorkspaceTrustPrompt trust)
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
            ProviderCatalog.CreateStarter().Overlay([provider]));
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
                chooser,
                trust),
            plugins);
        return new OpenedSession(session, observer);
    }

    private sealed record OpenedSession(CodingSession Session, RecordingSessionObserver Observer);
}
