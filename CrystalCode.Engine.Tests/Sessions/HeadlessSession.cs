using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// A real <see cref="CodingSession"/> wired to a scripted model and a
/// recording observer. No terminal exists anywhere in it.
/// </summary>
internal sealed class HeadlessSession : IDisposable
{
    private readonly TemporaryHome _home = new();
    private readonly TemporaryWorkspace _workspace = new();

    public HeadlessSession(
        IStreamingChatClient client,
        SessionDocument? resume = null,
        bool showCompactionSummary = true)
    {
        ArgumentNullException.ThrowIfNull(client);
        Observer = new RecordingSessionObserver();
        Approvals = new RecordingApprovalPrompt(ApprovalChoice.Deny);

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
        if (!showCompactionSummary)
        {
            settings = settings.WithShowCompactionSummary(false);
        }

        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(new ScriptedChatPlugin(client));

        if (resume is not null)
        {
            resume.Workspace = _workspace.Path;
            if (string.IsNullOrWhiteSpace(resume.Id))
            {
                resume.Id = SessionStore.NewId();
            }
        }

        Session = CodingSession.Create(
            settings,
            new SettingsStore(_home.Home),
            new CredentialStore(_home.Home),
            _home.Home,
            _workspace.Path,
            new SessionFrontEnd(
                Observer,
                Approvals,
                new FixedUserPrompt("unused"),
                new DecliningSessionChooser(),
                new ScriptedTrustPrompt(accept: false)),
            plugins,
            resume);
    }

    public SessionDocument ReadSaved()
    {
        var store = new SessionStore(_home.Home);
        if (!store.TryLoad(Session.SessionId, out var document))
        {
            throw new InvalidOperationException("Session was not saved.");
        }

        return document;
    }

    public CodingSession Session { get; }

    public RecordingSessionObserver Observer { get; }

    public RecordingApprovalPrompt Approvals { get; }

    public string WorkspacePath => _workspace.Path;

    public string SpaceDirectory => OperatorSpace.Resolve(_home.Home);

    /// <summary>
    /// Submits one prompt and drives the turn to its end, the way any front
    /// end does: submit, await the turn, then complete it.
    /// </summary>
    public async Task RunTurnAsync(string text)
    {
        var quit = await Session.SubmitAsync(text, CancellationToken.None);
        if (quit)
        {
            throw new InvalidOperationException("The prompt asked the session to quit.");
        }

        if (Session.TurnTask is { } turn)
        {
            await turn;
        }

        await Session.CompleteTurnAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        Session.Close();
        _workspace.Dispose();
        _home.Dispose();
    }
}
