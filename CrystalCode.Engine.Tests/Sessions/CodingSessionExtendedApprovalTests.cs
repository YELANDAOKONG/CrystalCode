using System.Text.Json;

using Crystal;
using Crystal.Chat;

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

public sealed class CodingSessionExtendedApprovalTests
{
    private const string Allow =
        """{"outcome":"allow","risk_level":"low","user_authorization":"high","rationale":"Fine."}""";

    private const string Deny =
        """{"outcome":"deny","risk_level":"high","user_authorization":"high","rationale":"Prints a secret."}""";

    [Fact]
    public async Task Review_RunsTheExtendedModelAfterAPrimaryAllow()
    {
        using var outside = new OutsideFile();
        var work = new RoutedClient(Allow, ToolRound(outside.Path), TextRound("Read it."));
        var primary = new RoutedClient(Allow);
        var extended = new RoutedClient(Allow);
        using var harness = Harness.Open(
            Settings(extended: Extended()),
            new() { ["work"] = work, ["reviewer"] = primary, ["judge"] = extended });

        await harness.RunTurnAsync("Read the outside file.");

        Assert.Equal(1, primary.CompleteCount);
        Assert.Equal(1, extended.CompleteCount);
        Assert.Contains(
            "## Primary review",
            extended.LastUserText,
            StringComparison.Ordinal);
        Assert.Contains("Rationale: Fine.", extended.LastUserText, StringComparison.Ordinal);
        Assert.Contains("extended approval reviewer", extended.LastSystemText, StringComparison.Ordinal);
        var passed = harness.Observer.Events.OfType<ApprovalPassed>().Single(item => item.Call.Name == "read");
        Assert.Equal(ApprovalPassReason.Extended, passed.Reason);
        Assert.NotNull(passed.ExtendedReview);
        Assert.Equal(
            [ApprovalReviewStage.Primary, ApprovalReviewStage.Extended],
            harness.Observer.Events.OfType<ApprovalReviewing>().Select(item => item.Stage));
    }

    [Fact]
    public async Task Review_ExtendedDenyRejectsTheCall()
    {
        using var outside = new OutsideFile();
        var work = new RoutedClient(Allow, ToolRound(outside.Path), TextRound("Stopped."));
        var extended = new RoutedClient(Deny);
        using var harness = Harness.Open(
            Settings(extended: Extended()),
            new() { ["work"] = work, ["reviewer"] = new RoutedClient(Allow), ["judge"] = extended });

        await harness.RunTurnAsync("Read the outside file.");

        var result = harness.Observer.Events.OfType<ToolResultsReceived>().Single().Results.Single();
        Assert.StartsWith(
            "The extended approval reviewer declined this action: Prints a secret.",
            result.Text,
            StringComparison.Ordinal);
        Assert.Equal(0, harness.Approvals.Count);
    }

    [Fact]
    public async Task Review_HidesThePrimaryVerdictWhenConfigured()
    {
        using var outside = new OutsideFile();
        var work = new RoutedClient(Allow, ToolRound(outside.Path), TextRound("Read it."));
        var extended = new RoutedClient(Allow);
        using var harness = Harness.Open(
            Settings(extended: Extended().WithIncludePrimaryReview(false)),
            new() { ["work"] = work, ["reviewer"] = new RoutedClient(Allow), ["judge"] = extended });

        await harness.RunTurnAsync("Read the outside file.");

        Assert.DoesNotContain("Rationale: Fine.", extended.LastUserText, StringComparison.Ordinal);
        Assert.Contains("Not shown.", extended.LastUserText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clients_UseEachReviewerOwnTierAndSpeed()
    {
        using var outside = new OutsideFile();
        var work = new RoutedClient(Allow, ToolRound(outside.Path), TextRound("Read it."));
        using var harness = Harness.Open(
            Settings(
                approval: new ApprovalModelSettings(true, "scripted", "reviewer", serviceTier: "priority"),
                extended: new ExtendedApprovalSettings(
                    new ApprovalModelSettings(true, "scripted", "judge", speed: "fast"))),
            new() { ["work"] = work, ["reviewer"] = new RoutedClient(Allow), ["judge"] = new RoutedClient(Allow) });

        await harness.RunTurnAsync("Read the outside file.");

        Assert.Contains(("reviewer", "priority", null), harness.Factory.Created);
        Assert.Contains(("judge", null, "fast"), harness.Factory.Created);
        Assert.Contains(("work", null, null), harness.Factory.Created);
    }

    [Fact]
    public async Task Commands_ConfigureExtendedApprovalAndPersistIt()
    {
        using var harness = Harness.Open(
            Settings(extended: null),
            new() { ["work"] = new RoutedClient(Allow), ["reviewer"] = new RoutedClient(Allow), ["judge"] = new RoutedClient(Allow) });
        await harness.Session.StartAsync(CancellationToken.None);

        await harness.Session.SubmitAsync("/approval extended", CancellationToken.None);
        Assert.Equal("Extended approval  Off", harness.LastNote());

        await harness.Session.SubmitAsync("/approval extended on", CancellationToken.None);
        Assert.Equal("Set an extended approval model before turning it on.", harness.LastError());

        await harness.Session.SubmitAsync("/approval extended judge", CancellationToken.None);
        Assert.Equal("Extended approval  On  scripted  judge", harness.LastNote());

        await harness.Session.SubmitAsync("/approval extended speed", CancellationToken.None);
        Assert.Equal("Extended approval speed  Fast", harness.LastNote());

        await harness.Session.SubmitAsync("/approval extended tier flex", CancellationToken.None);
        Assert.Equal("Service tier is not available for the extended approval model.", harness.LastError());

        await harness.Session.SubmitAsync("/approval extended trigger medium", CancellationToken.None);
        Assert.Equal("Extended approval trigger  Medium", harness.LastNote());

        await harness.Session.SubmitAsync("/approval extended trigger severe", CancellationToken.None);
        Assert.Equal("Extended approval trigger is low, medium, or high.", harness.LastError());

        await harness.Session.SubmitAsync("/approval extended primary off", CancellationToken.None);
        Assert.Equal("Extended approval primary review  Off", harness.LastNote());

        var config = File.ReadAllText(harness.Home.Home.ConfigPath);
        Assert.Contains("\"extendedApproval\"", config, StringComparison.Ordinal);
        Assert.Contains("\"speed\": \"fast\"", config, StringComparison.Ordinal);
        Assert.Contains("\"trigger\": \"medium\"", config, StringComparison.Ordinal);
        Assert.Contains("\"includePrimaryReview\": false", config, StringComparison.Ordinal);

        var menu = harness.Observer.Events.OfType<SlashCommandsChanged>().LastOrDefault()?.Commands
            ?? throw new InvalidOperationException("No slash menu was published.");
        var approval = menu.Single(item => item.Name == "approval");
        var extendedArgument = approval.ArgumentOptions.Single(item => item.Name == "extended");
        Assert.Contains(extendedArgument.ArgumentOptions, item => item.Name == "trigger");
        Assert.Contains(extendedArgument.ArgumentOptions, item => item.Name == "primary");
        Assert.Contains(extendedArgument.ArgumentOptions, item => item.Name == "judge");

        harness.Observer.Clear();
        await harness.Session.SubmitAsync("/status full", CancellationToken.None);
        var status = harness.Observer.Events.OfType<StatusReported>().Single().Status;
        Assert.Equal("scripted / judge  ·  Speed Fast", status.ExtendedApprovalModel);
        Assert.Equal("Medium", status.ExtendedApprovalTrigger);
        Assert.False(status.ExtendedApprovalPrimaryReview);

    }

    [Fact]
    public async Task Commands_SetTheApprovalModelTierAndSpeed()
    {
        using var harness = Harness.Open(
            Settings(extended: null),
            new() { ["work"] = new RoutedClient(Allow), ["reviewer"] = new RoutedClient(Allow), ["judge"] = new RoutedClient(Allow) });
        await harness.Session.StartAsync(CancellationToken.None);

        await harness.Session.SubmitAsync("/approval tier priority", CancellationToken.None);
        Assert.Equal("Approval tier  Priority", harness.LastNote());

        await harness.Session.SubmitAsync("/approval tier", CancellationToken.None);
        Assert.Equal("Approval tier  not set", harness.LastNote());

        await harness.Session.SubmitAsync("/approval speed fast", CancellationToken.None);
        Assert.Equal("Speed is not available for the approval model.", harness.LastError());

        await harness.Session.SubmitAsync("/approval model reviewer", CancellationToken.None);
        Assert.Equal("Approval model  On  scripted  reviewer", harness.LastNote());
    }

    [Fact]
    public async Task Chrome_FollowsTheApprovalAndExtendedApprovalSettings()
    {
        using var harness = Harness.Open(
            Settings(extended: null),
            new() { ["work"] = new RoutedClient(Allow), ["reviewer"] = new RoutedClient(Allow), ["judge"] = new RoutedClient(Allow) });
        await harness.Session.StartAsync(CancellationToken.None);
        Assert.Equal(new ReviewerChrome("scripted", "reviewer", string.Empty, string.Empty, string.Empty), harness.Chrome().Reviewer);
        Assert.Null(harness.Chrome().ExtendedApproval);

        await harness.Session.SubmitAsync("/approval tier priority", CancellationToken.None);
        Assert.Equal("Tier Priority", harness.Chrome().Reviewer!.ServiceTier);

        await harness.Session.SubmitAsync("/approval extended judge", CancellationToken.None);
        await harness.Session.SubmitAsync("/approval extended speed", CancellationToken.None);
        await harness.Session.SubmitAsync("/approval extended trigger medium", CancellationToken.None);
        var extended = harness.Chrome().ExtendedApproval!;
        Assert.Equal("judge", extended.Reviewer.Model);
        Assert.Equal("Speed Fast", extended.Reviewer.Speed);
        Assert.Equal("Medium", extended.Trigger);
        Assert.True(extended.IncludePrimaryReview);

        await harness.Session.SubmitAsync("/approval extended primary off", CancellationToken.None);
        Assert.False(harness.Chrome().ExtendedApproval!.IncludePrimaryReview);

        await harness.Session.SubmitAsync("/approval extended off", CancellationToken.None);
        Assert.Null(harness.Chrome().ExtendedApproval);
        Assert.NotNull(harness.Chrome().Reviewer);

        await harness.Session.SubmitAsync("/approval model off", CancellationToken.None);
        Assert.Null(harness.Chrome().Reviewer);
    }

    private static ExtendedApprovalSettings Extended() =>
        new(new ApprovalModelSettings(true, "scripted", "judge"));

    private static HarnessSettings Settings(
        ExtendedApprovalSettings? extended,
        ApprovalModelSettings? approval = null)
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ProviderProtocol.Ollama,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>
            {
                ["work"] = new(200_000),
                ["reviewer"] = new(200_000, serviceTiers: ["priority"]),
                ["judge"] = new(200_000, speeds: ["fast"])
            });
        return new HarnessSettings(
            provider.Name,
            "work",
            ApprovalMode.Review,
            0.8,
            new ProviderCatalog([provider]),
            skills: false,
            externalTools: false,
            approvalModel: approval ?? new ApprovalModelSettings(true, "scripted", "reviewer"),
            extendedApproval: extended);
    }

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
            JsonChatToolCallDelta(0, 0, "c1", "read", arguments),
            new ChatCandidateCompleted(0, FinishReason.ToolCalls)
        ];
    }

    private sealed class OutsideFile : IDisposable
    {
        public OutsideFile()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "crystal-extended-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(Path, "outside-text");
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TemporaryWorkspace _workspace;

        private Harness(
            CodingSession session,
            TemporaryHome home,
            TemporaryWorkspace workspace,
            RecordingSessionObserver observer,
            RecordingApprovalPrompt approvals,
            RoutingFactory factory)
        {
            Session = session;
            Home = home;
            _workspace = workspace;
            Observer = observer;
            Approvals = approvals;
            Factory = factory;
        }

        public CodingSession Session { get; }

        public TemporaryHome Home { get; }

        public RecordingSessionObserver Observer { get; }

        public RecordingApprovalPrompt Approvals { get; }

        public RoutingFactory Factory { get; }

        public static Harness Open(HarnessSettings settings, Dictionary<string, IStreamingChatClient> clients)
        {
            var home = new TemporaryHome();
            var workspace = new TemporaryWorkspace();
            var observer = new RecordingSessionObserver();
            var approvals = new RecordingApprovalPrompt(ApprovalChoice.Deny);
            var factory = new RoutingFactory(clients);
            var plugins = new PluginRegistry();
            plugins.Add(new WorkspaceToolsPlugin());
            plugins.Add(new RoutingPlugin(factory));
            var session = CodingSession.Create(
                settings,
                new DiskSettingsStore(home.Home),
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
            return new Harness(session, home, workspace, observer, approvals, factory);
        }

        public async Task RunTurnAsync(string text)
        {
            await Session.StartAsync(CancellationToken.None);
            Assert.False(await Session.SubmitAsync(text, CancellationToken.None));
            if (Session.TurnTask is { } turn)
            {
                await turn;
            }

            await Session.CompleteTurnAsync(CancellationToken.None);
        }

        public SessionChrome Chrome() => Observer.Events
            .Select(static item => item switch
            {
                SessionStarted started => started.Chrome,
                ChromeChanged changed => changed.Chrome,
                _ => null
            })
            .OfType<SessionChrome>()
            .Last();

        public string LastNote() => Observer.Events.OfType<NoteWritten>().Last().Text;

        public string LastError() => Observer.Events.OfType<ErrorWritten>().Last().Text;

        public void Dispose()
        {
            Session.Close();
            _workspace.Dispose();
            Home.Dispose();
        }
    }

    private sealed class RoutedClient : IStreamingChatClient
    {
        private readonly string _verdict;
        private readonly Queue<IReadOnlyList<ChatStreamEvent>> _rounds;

        public RoutedClient(string verdict, params IReadOnlyList<ChatStreamEvent>[] rounds)
        {
            _verdict = verdict;
            _rounds = new Queue<IReadOnlyList<ChatStreamEvent>>(rounds);
        }

        public int CompleteCount { get; private set; }

        public string LastSystemText { get; private set; } = string.Empty;

        public string LastUserText { get; private set; } = string.Empty;

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            var events = _rounds.Count == 0 ? Array.Empty<ChatStreamEvent>() : _rounds.Dequeue();
            return EnumerateAsync(events, cancellationToken);
        }

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCount++;
            foreach (var item in request.Items)
            {
                if (item is ChatMessage message && message.Role == ChatRole.System)
                {
                    LastSystemText = message.Text ?? string.Empty;
                }
                else if (item is ChatMessage user && user.Role == ChatRole.User)
                {
                    LastUserText = user.Text ?? string.Empty;
                }
            }

            return Task.FromResult(new ChatResponse(
            [
                new ChatCandidate([new ChatMessage(ChatRole.Assistant, _verdict)], FinishReason.Stop)
            ]));
        }

        private static async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
            IReadOnlyList<ChatStreamEvent> events,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var streamEvent in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return streamEvent;
                await Task.Yield();
            }
        }
    }

    private sealed class RoutingPlugin(RoutingFactory factory) : IPlugin
    {
        public string Name => "routing";

        public PluginContribution Contribute() => new(clients: [factory]);
    }

    private sealed class RoutingFactory(IReadOnlyDictionary<string, IStreamingChatClient> clients) : IChatClientFactory
    {
        public List<(string Model, string? Tier, string? Speed)> Created { get; } = [];

        public bool CanCreate(ProviderProtocol protocol) => protocol == ProviderProtocol.Ollama;

        public IStreamingChatClient Create(HarnessSettings settings, string apiKey)
        {
            Created.Add((settings.Model, settings.ServiceTier, settings.Speed));
            return clients[settings.Model];
        }
    }
}
