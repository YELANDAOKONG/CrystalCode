using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CodingSessionApprovalModelTests
{
    private const string AllowReview =
        """
        {
          "outcome": "allow",
          "risk_level": "low",
          "user_authorization": "high",
          "rationale": "The task asked for this file."
        }
        """;

    [Fact]
    public async Task Commands_ShowSelectAndToggleTheApprovalModel()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/approval model", CancellationToken.None);
        Assert.Equal("Approval model  Off", LastNote(headless));

        await headless.Session.SubmitAsync("/approval model on", CancellationToken.None);
        Assert.Equal("Set an approval model before turning it on.", LastError(headless));

        await headless.Session.SubmitAsync("/approval model missing", CancellationToken.None);
        Assert.Contains("/approval model", LastError(headless), StringComparison.Ordinal);
        Assert.DoesNotContain("Pass /model", LastError(headless), StringComparison.Ordinal);

        await headless.Session.SubmitAsync("/approval model model", CancellationToken.None);
        Assert.Equal("Approval model  On  scripted  model", LastNote(headless));
        Assert.Equal("scripted / model", await ReportedApprovalModelAsync(headless));

        await headless.Session.SubmitAsync("/approval model off", CancellationToken.None);
        Assert.Equal("Approval model  Off  scripted  model", LastNote(headless));
        Assert.Null(await ReportedApprovalModelAsync(headless));

        await headless.Session.SubmitAsync("/approval model on extra", CancellationToken.None);
        Assert.Equal(
            "Approval model is on or off, or a provider and model.",
            LastError(headless));
    }

    [Fact]
    public async Task Commands_SetAndCycleTheApprovalThinkingGear()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: HeadlessSession.ScriptedSettings(thinking: true));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/approval thinking low", CancellationToken.None);
        Assert.Equal(
            "Set an approval model before changing its thinking gear.",
            LastError(headless));

        await headless.Session.SubmitAsync("/approval model model", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval thinking", CancellationToken.None);
        Assert.Equal("Approval thinking  Off", LastNote(headless));

        await headless.Session.SubmitAsync("/approval thinking", CancellationToken.None);
        Assert.Equal("Approval thinking  Low", LastNote(headless));

        await headless.Session.SubmitAsync("/approval thinking", CancellationToken.None);
        Assert.Equal("Approval thinking  High", LastNote(headless));

        await headless.Session.SubmitAsync("/approval thinking", CancellationToken.None);
        Assert.Equal("Approval thinking  Default", LastNote(headless));

        await headless.Session.SubmitAsync("/approval thinking high", CancellationToken.None);
        Assert.Equal("Approval thinking  High", LastNote(headless));
        Assert.Contains(
            "\"thinkingEffort\": \"high\"",
            File.ReadAllText(headless.Home.ConfigPath),
            StringComparison.Ordinal);

        await headless.Session.SubmitAsync("/approval model model", CancellationToken.None);
        Assert.Equal(
            "Approval model  On  scripted  model  ·  Think High",
            LastNote(headless));

        var menu = headless.Observer.Events.OfType<SlashCommandsChanged>().Last().Commands;
        var approval = menu.Single(item => item.Name == "approval");
        var modelArgument = approval.ArgumentOptions.Single(item => item.Name == "model");
        Assert.Contains(modelArgument.ArgumentOptions, item => item.Name == "model");
        var thinkingArgument = approval.ArgumentOptions.Single(item => item.Name == "thinking");
        Assert.Contains(thinkingArgument.ArgumentOptions, item => item.Name == "high");

        await headless.Session.SubmitAsync("/approval thinking maximum", CancellationToken.None);
        Assert.Equal(
            "Thinking effort 'maximum' is not available for the approval model.",
            LastError(headless));

        var status = await ReportedStatusAsync(headless);
        Assert.Equal("scripted / model", status.ApprovalModel);
        Assert.Equal("Think High", status.ApprovalThinking);
    }

    [Fact]
    public async Task Commands_RejectApprovalThinkingForAModelWithoutThinking()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/approval model model", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval thinking low", CancellationToken.None);

        Assert.Equal("The approval model does not support thinking.", LastError(headless));
    }

    [Fact]
    public async Task Commands_RejectOffWhenTheApprovalModelCannotDisableThinking()
    {
        var model = new ModelSettings(
            200_000,
            thinking: true,
            thinkingEfforts: ["low", "high"],
            thinkingCanDisable: false);
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = model });
        var settings = new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/approval model model", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval thinking off", CancellationToken.None);

        Assert.Equal("The approval model cannot disable thinking.", LastError(headless));
    }

    [Fact]
    public async Task Review_UsesTheApprovalModelAndKeepsItAcrossWorkModelChanges()
    {
        var outside = Path.Combine(Path.GetTempPath(), "crystal-approval-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "outside-text");
        var work = new CountingClient(ToolRound(outside), TextRound("Read it."));
        var reviewer = new CountingClient();
        var other = new CountingClient(ToolRound(outside), TextRound("Read it again."));
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var observer = new RecordingSessionObserver();
        var approvals = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var session = Open(
            Settings(enabled: true),
            new Dictionary<string, IStreamingChatClient>
            {
                ["work"] = work,
                ["reviewer"] = reviewer,
                ["other"] = other
            },
            home,
            workspace,
            observer,
            approvals);
        try
        {
            await session.StartAsync(CancellationToken.None);
            await RunTurnAsync(session, "Read the outside file.");

            Assert.Equal(1, reviewer.CompleteCount);
            Assert.Equal(0, reviewer.StreamCount);
            Assert.Equal(0, work.CompleteCount);
            Assert.Equal(2, work.StreamCount);
            Assert.Equal(ReasoningMode.Automatic, reviewer.LastComplete?.Reasoning?.Mode);
            Assert.Equal(ReasoningMode.Enabled, work.LastStream?.Reasoning?.Mode);
            Assert.Equal(ReasoningEffort.High, work.LastStream?.Reasoning?.Effort);
            Assert.Equal(0, approvals.Count);
            Assert.Equal(1, approvals.ReviewCount);
            Assert.Contains(
                "outside-text",
                observer.Events.OfType<ToolResultsReceived>().Single().Results.Single().Text,
                StringComparison.Ordinal);

            await session.SubmitAsync("/model other", CancellationToken.None);
            observer.Clear();
            await RunTurnAsync(session, "Read it once more.");

            Assert.Equal(2, reviewer.CompleteCount);
            Assert.Equal(0, reviewer.StreamCount);
            Assert.Equal(2, work.StreamCount);
            Assert.Equal(0, work.CompleteCount);
            Assert.Equal(2, other.StreamCount);
            Assert.Equal(0, other.CompleteCount);
            Assert.Equal(ReasoningMode.Automatic, reviewer.LastComplete?.Reasoning?.Mode);
        }
        finally
        {
            session.Close();
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Review_FollowsTheWorkModelWhileTheApprovalModelIsOff()
    {
        var outside = Path.Combine(Path.GetTempPath(), "crystal-approval-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "outside-text");
        var work = new CountingClient(ToolRound(outside), TextRound("Read it."));
        var reviewer = new CountingClient();
        var other = new CountingClient(ToolRound(outside), TextRound("Read it again."));
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var observer = new RecordingSessionObserver();
        var approvals = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var session = Open(
            Settings(enabled: false),
            new Dictionary<string, IStreamingChatClient>
            {
                ["work"] = work,
                ["reviewer"] = reviewer,
                ["other"] = other
            },
            home,
            workspace,
            observer,
            approvals);
        try
        {
            await session.StartAsync(CancellationToken.None);
            await RunTurnAsync(session, "Read the outside file.");

            Assert.Equal(0, reviewer.CompleteCount);
            Assert.Equal(0, reviewer.StreamCount);
            Assert.Equal(1, work.CompleteCount);
            Assert.Equal(ReasoningMode.Enabled, work.LastComplete?.Reasoning?.Mode);
            Assert.Equal(ReasoningEffort.High, work.LastComplete?.Reasoning?.Effort);

            await session.SubmitAsync("/model other", CancellationToken.None);
            await RunTurnAsync(session, "Read it once more.");

            Assert.Equal(1, work.CompleteCount);
            Assert.Equal(1, other.CompleteCount);
            Assert.Equal(0, reviewer.CompleteCount);
        }
        finally
        {
            session.Close();
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Review_UsesTheConfiguredApprovalThinkingGear()
    {
        var outside = Path.Combine(Path.GetTempPath(), "crystal-approval-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "outside-text");
        var work = new CountingClient(ToolRound(outside), TextRound("Read it."));
        var reviewer = new CountingClient();
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var observer = new RecordingSessionObserver();
        var approvals = new RecordingApprovalPrompt(ApprovalChoice.Deny);
        var session = Open(
            Settings(enabled: true, approvalThinking: ThinkingSelection.Parse("high")),
            new Dictionary<string, IStreamingChatClient>
            {
                ["work"] = work,
                ["reviewer"] = reviewer
            },
            home,
            workspace,
            observer,
            approvals);
        try
        {
            await session.StartAsync(CancellationToken.None);
            await RunTurnAsync(session, "Read the outside file.");

            Assert.Equal(1, reviewer.CompleteCount);
            Assert.Equal(0, reviewer.StreamCount);
            Assert.Equal(ReasoningMode.Enabled, reviewer.LastComplete?.Reasoning?.Mode);
            Assert.Equal(ReasoningEffort.High, reviewer.LastComplete?.Reasoning?.Effort);
        }
        finally
        {
            session.Close();
            File.Delete(outside);
        }
    }

    [Fact]
    public void Create_FailsWhenTheEnabledApprovalModelHasNoCredential()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var catalog = new ProviderCatalog(
        [
            ScriptedProvider(),
            new ProviderDefinition(
                new ProviderName("gated"),
                ProviderProtocol.OpenAI,
                new Uri("https://example.invalid/v1/"),
                new Dictionary<string, ModelSettings> { ["gated"] = ThinkingModel() },
                apiKeyEnvironment: "ZZ_CRYSTAL_APPROVAL_MODEL_TEST_KEY",
                requiresApiKey: true)
        ]);
        var settings = new HarnessSettings(
            new ProviderName("scripted"),
            "work",
            ApprovalMode.Review,
            0.8,
            catalog,
            thinkingEffort: ThinkingSelection.Parse("high"),
            skills: false,
            externalTools: false,
            approvalModel: new ApprovalModelSettings(true, "gated", "gated"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Open(
                settings,
                new Dictionary<string, IStreamingChatClient>
                {
                    ["work"] = new CountingClient(),
                    ["gated"] = new CountingClient()
                },
                home,
                workspace,
                new RecordingSessionObserver(),
                new RecordingApprovalPrompt(ApprovalChoice.Deny)));

        Assert.Contains("Missing API key", exception.Message, StringComparison.Ordinal);
    }

    private static HarnessSettings Settings(
        bool enabled,
        ThinkingSelection? approvalThinking = null)
    {
        var selection = new ApprovalModelSettings(
            enabled,
            "scripted",
            "reviewer",
            approvalThinking);
        return new HarnessSettings(
            new ProviderName("scripted"),
            "work",
            ApprovalMode.Review,
            0.8,
            new ProviderCatalog([ScriptedProvider()]),
            thinkingEffort: ThinkingSelection.Parse("high"),
            skills: false,
            externalTools: false,
            approvalModel: selection);
    }

    private static ProviderDefinition ScriptedProvider() =>
        new(
            new ProviderName("scripted"),
            ProviderProtocol.Ollama,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>
            {
                ["work"] = ThinkingModel(),
                ["reviewer"] = ThinkingModel(),
                ["other"] = ThinkingModel()
            });

    private static ModelSettings ThinkingModel() =>
        new(200_000, thinking: true, thinkingEfforts: ["low", "high"]);

    private static CodingSession Open(
        HarnessSettings settings,
        IReadOnlyDictionary<string, IStreamingChatClient> clients,
        TemporaryHome home,
        TemporaryWorkspace workspace,
        RecordingSessionObserver observer,
        RecordingApprovalPrompt approvals)
    {
        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(new RoutingPlugin(clients));
        return CodingSession.Create(
            settings,
            new SettingsStore(home.Home),
            new CredentialStore(home.Home),
            home.Home,
            workspace.Path,
            new SessionFrontEnd(
                observer,
                approvals,
                new FixedUserPrompt("unused"),
                new DecliningSessionChooser(),
                new ScriptedTrustPrompt(accept: false)),
            plugins);
    }

    private static async Task RunTurnAsync(CodingSession session, string text)
    {
        var quit = await session.SubmitAsync(text, CancellationToken.None);
        Assert.False(quit);
        if (session.TurnTask is { } turn)
        {
            await turn;
        }

        await session.CompleteTurnAsync(CancellationToken.None);
    }

    private static async Task<string?> ReportedApprovalModelAsync(HeadlessSession headless)
    {
        headless.Observer.Clear();
        var quit = await headless.Session.SubmitAsync("/status", CancellationToken.None);
        Assert.False(quit);
        return headless.Observer.Events.OfType<StatusReported>().Single().Status.ApprovalModel;
    }

    private static async Task<SessionStatus> ReportedStatusAsync(HeadlessSession headless)
    {
        headless.Observer.Clear();
        var quit = await headless.Session.SubmitAsync("/status", CancellationToken.None);
        Assert.False(quit);
        return headless.Observer.Events.OfType<StatusReported>().Single().Status;
    }

    private static string LastNote(HeadlessSession headless) =>
        headless.Observer.Events.OfType<NoteWritten>().Last().Text;

    private static string LastError(HeadlessSession headless) =>
        headless.Observer.Events.OfType<ErrorWritten>().Last().Text;

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private static ChatStreamEvent[] ToolRound(string path)
    {
        var arguments = JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = path });
        return
        [
            new ChatToolCallDelta(0, 0, "c1", "read", arguments),
            new ChatCandidateCompleted(0, FinishReason.ToolCalls)
        ];
    }

    private sealed class CountingClient : IStreamingChatClient
    {
        private readonly Queue<IReadOnlyList<ChatStreamEvent>> _rounds;

        public CountingClient(params IReadOnlyList<ChatStreamEvent>[] rounds)
        {
            _rounds = new Queue<IReadOnlyList<ChatStreamEvent>>(rounds);
        }

        public int StreamCount { get; private set; }

        public int CompleteCount { get; private set; }

        public ChatRequest? LastStream { get; private set; }

        public ChatRequest? LastComplete { get; private set; }

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StreamCount++;
            LastStream = request;
            var events = _rounds.Count == 0
                ? Array.Empty<ChatStreamEvent>()
                : _rounds.Dequeue();
            return EnumerateAsync(events, cancellationToken);
        }

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCount++;
            LastComplete = request;
            return Task.FromResult(
                new ChatResponse(
                [
                    new ChatCandidate(
                        [new ChatMessage(ChatRole.Assistant, AllowReview)],
                        FinishReason.Stop)
                ]));
        }

        private static async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
            IReadOnlyList<ChatStreamEvent> events,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            foreach (var streamEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return streamEvent;
                await Task.Yield();
            }
        }
    }

    private sealed class RoutingPlugin : IPlugin
    {
        private readonly IReadOnlyDictionary<string, IStreamingChatClient> _clients;

        public RoutingPlugin(IReadOnlyDictionary<string, IStreamingChatClient> clients)
        {
            _clients = clients;
        }

        public string Name => "routing";

        public PluginContribution Contribute() =>
            new(clients: [new RoutingFactory(_clients)]);
    }

    private sealed class RoutingFactory : IChatClientFactory
    {
        private readonly IReadOnlyDictionary<string, IStreamingChatClient> _clients;

        public RoutingFactory(IReadOnlyDictionary<string, IStreamingChatClient> clients)
        {
            _clients = clients;
        }

        public bool CanCreate(ProviderProtocol protocol) =>
            protocol == ProviderProtocol.Ollama;

        public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return _clients[settings.Model];
        }
    }
}
