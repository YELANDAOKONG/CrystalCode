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
        bool showCompactionSummary = true,
        HarnessSettings? settings = null,
        HarnessSettings? persistedSettings = null,
        Action<CrystalHome, string>? prepare = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        Observer = new RecordingSessionObserver();
        Approvals = new RecordingApprovalPrompt(ApprovalChoice.Deny);

        var sessionSettings = settings ?? ScriptedSettings();
        if (settings is null && !showCompactionSummary)
        {
            sessionSettings = sessionSettings.WithShowCompactionSummary(false);
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

        prepare?.Invoke(_home.Home, _workspace.Path);

        Session = CodingSession.Create(
            sessionSettings,
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
            resume,
            persistedSettings);
    }

    public static HarnessSettings ScriptedSettings(bool thinking = false)
    {
        var model = thinking
            ? new ModelSettings(200_000, thinking: true, thinkingEfforts: ["low", "high"])
            : new ModelSettings(200_000);
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = model });
        return new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]));
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

    public CrystalHome Home => _home.Home;

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
