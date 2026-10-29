using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Compaction;
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

public sealed class CodingSessionCompactionModelTests
{
    [Fact]
    public async Task Commands_SetTheCompactionModelAndKeepUnknownModelsWhileOff()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: HeadlessSession.ScriptedSettings(thinking: true));
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/compact model", CancellationToken.None);
        Assert.Equal("Compaction model  Off", LastNote(headless));

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact model missing", CancellationToken.None);
        Assert.Contains("missing", LastError(headless), StringComparison.Ordinal);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact model model", CancellationToken.None);
        Assert.Contains("On", LastNote(headless), StringComparison.Ordinal);
        Assert.Contains("scripted", LastNote(headless), StringComparison.Ordinal);
        Assert.Contains(
            new CompactionLimits(200_000).SummaryOutputCap(),
            headless.ChatPlugin.OutputCaps);
        Assert.DoesNotContain(
            "outputTokenCap",
            File.ReadAllText(headless.Home.ConfigPath),
            StringComparison.Ordinal);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact thinking off", CancellationToken.None);
        Assert.Equal("Compaction thinking  Off", LastNote(headless));

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/status", CancellationToken.None);
        var status = headless.Observer.Events.OfType<StatusReported>().Single().Status;
        Assert.Contains("scripted", status.CompactionModel, StringComparison.Ordinal);
        Assert.Contains("model", status.CompactionModel, StringComparison.Ordinal);
        Assert.Contains("Think Off", status.CompactionModel, StringComparison.Ordinal);

        await headless.Session.SubmitAsync("/compact model off", CancellationToken.None);
        var saved = Read(headless);
        Assert.NotNull(saved.CompactionModel);
        Assert.Null(saved.CompactionModel!.Enabled);
        Assert.Equal("off", saved.CompactionModel.ThinkingEffort);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/status", CancellationToken.None);
        Assert.Null(
            headless.Observer.Events.OfType<StatusReported>().Single().Status.CompactionModel);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact later", CancellationToken.None);
        Assert.Equal(
            "Compact accepts no argument, or full, model, thinking, summary, auto, and micro.",
            LastError(headless));
    }

    [Fact]
    public async Task CompactModel_CapsTheSummaryClientForTheSelectedModel()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>
            {
                ["model"] = new(200_000, maxTokens: 128_000),
                ["brief"] = new(32_000, maxTokens: 4_096)
            },
            requiresApiKey: false);
        var settings = new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]));
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);
        var started = headless.ChatPlugin.OutputCaps.Count;

        await headless.Session.SubmitAsync("/compact model brief", CancellationToken.None);

        var expected = new CompactionLimits(
            200_000,
            maxTokens: 128_000,
            summaryWindow: 32_000,
            summaryMaxTokens: 4_096,
            useSummaryMaxTokens: true).SummaryOutputCap();
        Assert.Equal(expected, headless.ChatPlugin.OutputCaps[^1]);
        var created = headless.ChatPlugin.OutputCaps.Count;
        Assert.True(created > started);

        await headless.Session.SubmitAsync("/compact", CancellationToken.None);
        Assert.Equal(created, headless.ChatPlugin.OutputCaps.Count);
    }

    [Fact]
    public async Task CompactSummary_TogglesManualAndAutomaticDisplay()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/compact summary off", CancellationToken.None);
        Assert.Equal("Compaction summary  Off", LastNote(headless));
        Assert.False(Read(headless).ShowCompactionSummary);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact summary", CancellationToken.None);
        Assert.Equal("Compaction summary  On", LastNote(headless));
        Assert.Null(Read(headless).ShowCompactionSummary);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/summarize auto", CancellationToken.None);
        Assert.Equal("Automatic compaction summary  On", LastNote(headless));
        Assert.True(Read(headless).ShowAutomaticCompactionSummary);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact automatic off", CancellationToken.None);
        Assert.Equal("Automatic compaction summary  Off", LastNote(headless));
        Assert.Null(Read(headless).ShowAutomaticCompactionSummary);
        Assert.Null(Read(headless).ShowCompactionSummary);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact summary later", CancellationToken.None);
        Assert.Equal("Compaction summary is on or off.", LastError(headless));
    }

    [Fact]
    public async Task CompactMicro_TogglesAndShowsStatusOnlyWhileOn()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/status", CancellationToken.None);
        Assert.Null(headless.Observer.Events.OfType<StatusReported>().Single().Status.MicroCompaction);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact micro", CancellationToken.None);
        Assert.Equal("Microcompaction  On", LastNote(headless));
        Assert.True(Read(headless).MicroCompaction!.Enabled);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/status", CancellationToken.None);
        Assert.Equal(
            "On  2 turns, protect 40000, minimum 20000",
            headless.Observer.Events.OfType<StatusReported>().Single().Status.MicroCompaction);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact micro off", CancellationToken.None);
        Assert.Equal("Microcompaction  Off", LastNote(headless));
        Assert.Null(Read(headless).MicroCompaction);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/compact micro later", CancellationToken.None);
        Assert.Equal("Microcompaction is on or off.", LastError(headless));
    }

    [Fact]
    public async Task Trim_SwitchesAutomaticTrimmingAndRejectsBadArguments()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/trim off", CancellationToken.None);
        Assert.Equal("Image trim  Off", LastNote(headless));
        Assert.False(Read(headless).ImageTrim!.Enabled);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/trim on", CancellationToken.None);
        Assert.Equal("Image trim  On", LastNote(headless));
        Assert.Null(Read(headless).ImageTrim);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/trim 2", CancellationToken.None);
        Assert.Equal("No images to trim", LastNote(headless));

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/trim later", CancellationToken.None);
        Assert.StartsWith("Trim takes", LastError(headless), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Microcompaction_KeepsTheTurnTranscriptAfterPruning()
    {
        var client = new ScriptedStreamingClient(
        [
            new ChatTextDelta(0, 0, ChatRole.Assistant, "kept going"),
            new ChatCandidateCompleted(0, FinishReason.Stop)
        ]);
        var resume = new SessionDocument
        {
            Items =
            [
                new SessionItemDocument { Kind = "message", Role = "user", Text = "old" },
                new SessionItemDocument
                {
                    Kind = "tool_result",
                    CallId = "1",
                    Text = new string('x', 80)
                },
                new SessionItemDocument { Kind = "message", Role = "user", Text = "KEEP-THIS" }
            ]
        };
        var settings = HeadlessSession.ScriptedSettings().WithMicroCompaction(
            new MicroCompactionSettings(true, protectTurns: 1, protectTokens: 0, minimumTokens: 0));
        using var headless = new HeadlessSession(client, resume, settings: settings);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("continue");

        Assert.NotNull(client.LastRequest);
        Assert.Contains(
            client.LastRequest.Items.OfType<ChatMessage>(),
            message => message.Text.Contains("KEEP-THIS", StringComparison.Ordinal));
        Assert.Contains(
            client.LastRequest.Items.OfType<ToolResult>(),
            result => result.Text == ContextCompactor.OmittedResultText);
        var saved = headless.ReadSaved();
        Assert.Contains(saved.Items, item => item.Text == "KEEP-THIS");
        Assert.Contains(saved.Items, item => item.Text == "kept going");
        Assert.Contains(saved.Archive!, item => item.Text == new string('x', 80));
    }

    [Fact]
    public async Task Compact_UsesTheDedicatedClientAndKeepsItWhenTheSessionModelChanges()
    {
        var work = new SummaryClient(TextRound("Hello."), TextRound("Again."));
        var other = new SummaryClient(TextRound("There."));
        var summarizer = new SummaryClient();
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var observer = new RecordingSessionObserver();
        var session = Open(
            Settings(),
            new Dictionary<string, IStreamingChatClient>
            {
                ["work"] = work,
                ["other"] = other,
                ["summarizer"] = summarizer
            },
            home,
            workspace,
            observer);
        await session.StartAsync(CancellationToken.None);
        await RunTurnAsync(session, "first");
        await RunTurnAsync(session, "second");

        await session.SubmitAsync("/compact", CancellationToken.None);

        Assert.Equal(0, work.CompleteCount);
        Assert.Equal(1, summarizer.CompleteCount);
        Assert.Equal(ReasoningMode.Enabled, summarizer.LastComplete!.Reasoning!.Mode);
        Assert.Equal("high", summarizer.LastComplete.Reasoning.Effort!.Value);

        await session.SubmitAsync("/model other", CancellationToken.None);
        await RunTurnAsync(session, "third");
        await session.SubmitAsync("/compact", CancellationToken.None);

        Assert.Equal(0, other.CompleteCount);
        Assert.Equal(2, summarizer.CompleteCount);
        Assert.Equal(2, work.StreamCount);
        Assert.Equal(1, other.StreamCount);
    }

    [Fact]
    public async Task CompactFull_SummarizesTheLatestTurnToo()
    {
        var work = new SummaryClient(TextRound("Hello."));
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var observer = new RecordingSessionObserver();
        var session = Open(
            Settings().WithCompactionModel(new CompactionModelSettings(false, "scripted", "summarizer")),
            new Dictionary<string, IStreamingChatClient>
            {
                ["work"] = work,
                ["summarizer"] = new SummaryClient()
            },
            home,
            workspace,
            observer);
        await session.StartAsync(CancellationToken.None);
        await RunTurnAsync(session, "only");

        await session.SubmitAsync("/compact", CancellationToken.None);
        Assert.Equal(0, work.CompleteCount);

        await session.SubmitAsync("/compact full on", CancellationToken.None);
        Assert.Equal(0, work.CompleteCount);
        Assert.Contains(
            observer.Events.OfType<ErrorWritten>(),
            error => error.Text == "Full compaction takes no argument.");

        await session.SubmitAsync("/compact full", CancellationToken.None);

        Assert.Equal(1, work.CompleteCount);
        Assert.Contains("only", ((ChatMessage)work.LastComplete!.Items[^1]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compact_OmitsReasoningWhenTheCompactionModelIsOff()
    {
        var work = new SummaryClient(TextRound("Hello."), TextRound("Again."));
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var session = Open(
            Settings().WithCompactionModel(new CompactionModelSettings(false, "scripted", "summarizer")),
            new Dictionary<string, IStreamingChatClient>
            {
                ["work"] = work,
                ["summarizer"] = new SummaryClient()
            },
            home,
            workspace,
            new RecordingSessionObserver());
        await session.StartAsync(CancellationToken.None);
        await RunTurnAsync(session, "first");
        await RunTurnAsync(session, "second");

        await session.SubmitAsync("/compact", CancellationToken.None);

        Assert.Equal(1, work.CompleteCount);
        Assert.Null(work.LastComplete!.Reasoning);
    }

    private static HarnessSettings Settings()
    {
        var work = new ModelSettings(200_000, thinking: true, thinkingEfforts: ["low", "high"]);
        var summarizer = new ModelSettings(8_000, thinking: true, thinkingEfforts: ["low", "high"]);
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>
            {
                ["work"] = work,
                ["other"] = work,
                ["summarizer"] = summarizer
            });
        return new HarnessSettings(
            provider.Name,
            "work",
            CrystalCode.Engine.Approvals.ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]),
            compactionModel: new CompactionModelSettings(
                true,
                "scripted",
                "summarizer",
                ThinkingSelection.Parse("high")));
    }

    private static CodingSession Open(
        HarnessSettings settings,
        IReadOnlyDictionary<string, IStreamingChatClient> clients,
        TemporaryHome home,
        TemporaryWorkspace workspace,
        RecordingSessionObserver observer)
    {
        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(new RoutingPlugin(clients));
        return CodingSession.Create(
            settings,
            new DiskSettingsStore(home.Home),
            new CredentialStore(home.Home),
            home.Home,
            workspace.Path,
            new SessionFrontEnd(
                observer,
                new RecordingApprovalPrompt(CrystalCode.Engine.Approvals.ApprovalChoice.Deny),
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

    private static string LastNote(HeadlessSession headless) =>
        headless.Observer.Events.OfType<NoteWritten>().Last().Text;

    private static string LastError(HeadlessSession headless) =>
        headless.Observer.Events.OfType<ErrorWritten>().Last().Text;

    private static SettingsDocument Read(HeadlessSession headless)
    {
        var json = File.ReadAllText(headless.Home.ConfigPath);
        return JsonSerializer.Deserialize<SettingsDocument>(json, HomeJson.Options)
            ?? throw new InvalidOperationException("Settings file was empty.");
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private sealed class SummaryClient : IStreamingChatClient
    {
        private readonly Queue<IReadOnlyList<ChatStreamEvent>> _rounds;

        public SummaryClient(params IReadOnlyList<ChatStreamEvent>[] rounds)
        {
            _rounds = new Queue<IReadOnlyList<ChatStreamEvent>>(rounds);
        }

        public int StreamCount { get; private set; }

        public int CompleteCount { get; private set; }

        public ChatRequest? LastComplete { get; private set; }

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StreamCount++;
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
                        [new ChatMessage(ChatRole.Assistant, "## Objective\n- Kept.")],
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
