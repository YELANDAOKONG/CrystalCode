using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Modes;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Mode, approval, thinking, tier, speed, context, and model chosen while a
/// turn runs apply when that turn is collected, before a queued follow-up.
/// </summary>
public sealed class NextTurnSettingsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Plan_DuringATurnKeepsTheRunningModeAndAppliesNextTurn()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync("/plan", CancellationToken.None);

        Assert.False(headless.Session.PlanMode);
        Assert.Equal(ModeCatalog.Plan, headless.Session.PendingSettings.Mode);
        var chrome = headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome;
        Assert.False(chrome.PlanMode);
        Assert.Equal(ModeCatalog.Plan, chrome.Pending!.Mode);
        Assert.Contains(headless.Observer.Events.OfType<NoteWritten>(), note => note.Text == "Mode  Plan  Applies next turn");

        await EndTurnAsync(headless);

        Assert.True(headless.Session.PlanMode);
        Assert.True(headless.Session.PendingSettings.IsEmpty);
        Assert.True(headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Pending!.IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CycleMode_MovesFromThePendingModeAndBackToTheActiveOneClearsIt(bool backward)
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await StartTurnAsync(headless, client);

        Assert.False(headless.Session.CycleMode());
        Assert.Equal(ModeCatalog.Plan, headless.Session.PendingSettings.Mode);
        headless.Session.CycleMode(backward);

        Assert.True(headless.Session.PendingSettings.IsEmpty);
        await EndTurnAsync(headless);
        Assert.False(headless.Session.PlanMode);
    }

    [Fact]
    public async Task Approval_LastChoiceWinsAndIsSavedOnlyWhenApplied()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync("/approval edit", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval full", CancellationToken.None);

        Assert.Equal(ApprovalMode.Default, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Approval);
        Assert.Equal("full", headless.Session.PendingSettings.Approval);
        Assert.False(SavedContains(headless, "\"full\""));

        await EndTurnAsync(headless);

        Assert.Equal(ApprovalMode.Full, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Approval);
        Assert.True(SavedContains(headless, "\"full\""));
    }

    [Fact]
    public async Task Approval_CyclesFromThePendingLevel()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync("/approval", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval", CancellationToken.None);

        Assert.Equal(ApprovalMode.Next(ApprovalMode.Next(ApprovalMode.Default)).Value, headless.Session.PendingSettings.Approval);
        await EndTurnAsync(headless);
    }

    [Fact]
    public async Task Approval_NamingTheActiveLevelClearsThePendingOne()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync("/approval full", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval default", CancellationToken.None);

        Assert.True(headless.Session.PendingSettings.IsEmpty);
        await EndTurnAsync(headless);
        Assert.Equal(ApprovalMode.Default, headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Approval);
    }

    [Fact]
    public async Task Thinking_InvalidValueIsRefusedWhenTyped()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client, settings: HeadlessSession.ScriptedSettings(thinking: true));
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync("/thinking medium", CancellationToken.None);
        await headless.Session.SubmitAsync("/thinking high", CancellationToken.None);

        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("medium", StringComparison.Ordinal));
        Assert.Equal("high", headless.Session.PendingSettings.Thinking);
        await EndTurnAsync(headless);
        Assert.Equal("Think High", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Thinking);
    }

    [Fact]
    public async Task ExtendedApproval_WhileBusyValidatesAndKeepsTheLastChoice()
    {
        var client = new BlockingStreamingClient();
        var settings = HeadlessSession.ScriptedSettings(thinking: true)
            .WithExtendedApproval(new ExtendedApprovalSettings(new ApprovalModelSettings(true, "scripted", "model")));
        using var headless = new HeadlessSession(client, settings: settings);
        await StartTurnAsync(headless, client);

        await headless.Session.EnqueueAsync("/approval extended thinking low", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval extended thinking", CancellationToken.None);
        await headless.Session.EnqueueAsync("/approval extended thinking medium", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval extended trigger medium", CancellationToken.None);
        await headless.Session.EnqueueAsync("/approval extended trigger high", CancellationToken.None);
        await headless.Session.SubmitAsync("/approval extended trigger low", CancellationToken.None);
        await headless.Session.EnqueueAsync("/approval extended primary off", CancellationToken.None);

        var chrome = headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome;
        Assert.Empty(chrome.ExtendedApproval!.Reviewer.Thinking);
        Assert.True(chrome.ExtendedApproval.IncludePrimaryReview);
        Assert.Equal("high", chrome.Pending!.ExtendedApprovalThinking);
        Assert.Null(chrome.Pending.ExtendedApprovalTrigger);
        Assert.Equal(false, chrome.Pending.ExtendedApprovalPrimary);
        Assert.Equal("Next: Extended thinking High, Extended primary review Off", PendingSettingsText.Summary(chrome.Pending));
        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains("Thinking effort 'medium'", StringComparison.Ordinal));
        Assert.False(File.Exists(headless.Home.ConfigPath));

        await EndTurnAsync(headless);

        var applied = headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome;
        Assert.Equal("Think High", applied.ExtendedApproval!.Reviewer.Thinking);
        Assert.False(applied.ExtendedApproval.IncludePrimaryReview);
        Assert.True(applied.Pending!.IsEmpty);
        Assert.True(SavedContains(headless, "\"thinkingEffort\": \"high\""));
        Assert.True(SavedContains(headless, "\"includePrimaryReview\": false"));
    }

    [Theory]
    [InlineData("/logout scripted")]
    [InlineData("/trust off")]
    [InlineData("/topic set Changed")]
    [InlineData("/topic thinking high")]
    [InlineData("/notify off")]
    [InlineData("/approval extended off")]
    public async Task BusyInput_RefusesMutationsThroughBothPaths(string command)
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync(command, CancellationToken.None);
        await headless.Session.EnqueueAsync(command, CancellationToken.None);

        var expected = "Finish the current turn before using " + command.Split(' ')[0] + ".";
        Assert.Equal([expected, expected], headless.Observer.Events.OfType<ErrorWritten>().Select(error => error.Text));
        Assert.False(File.Exists(headless.Home.ConfigPath));
        await EndTurnAsync(headless);
    }

    [Fact]
    public async Task Model_DuringATurnSwitchesWhenTheTurnEnds()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client, settings: TwoModels());
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync("/model other", CancellationToken.None);

        Assert.Equal("model", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Model);
        Assert.Equal("other", headless.Session.PendingSettings.Model);
        await EndTurnAsync(headless);
        Assert.Equal("other", headless.Observer.Events.OfType<ChromeChanged>().Last().Chrome.Model);
        Assert.True(SavedContains(headless, "\"other\""));
    }

    [Fact]
    public async Task Model_ChoiceDropsThePendingSlotsOfThePreviousChoice()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client, settings: TwoModels());
        await StartTurnAsync(headless, client);

        await headless.Session.SubmitAsync("/tier flex", CancellationToken.None);
        await headless.Session.SubmitAsync("/model other", CancellationToken.None);

        Assert.Null(headless.Session.PendingSettings.ServiceTier);
        await headless.Session.SubmitAsync("/tier flex", CancellationToken.None);
        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("not available", StringComparison.Ordinal));
        await EndTurnAsync(headless);
    }

    [Fact]
    public async Task Runner_ReportsARecordedSettingAndARefusal()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);

        await runner.SubmitAsync("long task").WaitAsync(Wait);
        await client.Started.WaitAsync(Wait);
        var plan = await runner.SubmitAsync("/plan").WaitAsync(Wait);
        var refused = await runner.SubmitAsync("/cd ..").WaitAsync(Wait);
        var bad = await runner.SubmitAsync("/approval nope").WaitAsync(Wait);

        Assert.True(plan.Accepted);
        Assert.True(plan.Pending);
        Assert.False(refused.Accepted);
        Assert.False(refused.Pending);
        Assert.Contains("Finish the current turn", refused.Error, StringComparison.Ordinal);
        Assert.False(bad.Accepted);
        await stop.CancelAsync();
        await running.WaitAsync(Wait);
        Assert.True(headless.Session.PlanMode);
    }

    [Fact]
    public async Task Runner_AnIdleSettingIsNotPending()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);

        var plan = runner.SubmitAsync("/plan");
        runner.Complete();
        await runner.RunAsync(CancellationToken.None).WaitAsync(Wait);

        var result = await plan;
        Assert.True(result.Accepted);
        Assert.False(result.Pending);
        Assert.True(headless.Session.PlanMode);
    }

    [Fact]
    public async Task Pending_AppliesBeforeAQueuedFollowUpStarts()
    {
        var client = new HoldingStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        var runner = new SessionRunner(headless.Session);
        using var stop = new CancellationTokenSource();
        var running = runner.RunAsync(stop.Token);

        await runner.SubmitAsync("first").WaitAsync(Wait);
        await client.Started.WaitAsync(Wait);
        await runner.SubmitAsync("/plan").WaitAsync(Wait);
        await runner.SubmitAsync("second").WaitAsync(Wait);
        client.Release();
        await client.Second.WaitAsync(Wait);

        Assert.True(client.SecondPlan);
        await stop.CancelAsync();
        await running.WaitAsync(Wait);
    }

    private static async Task StartTurnAsync(HeadlessSession headless, BlockingStreamingClient client)
    {
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("long task", CancellationToken.None);
        await client.Started.WaitAsync(Wait);
    }

    private static async Task EndTurnAsync(HeadlessSession headless)
    {
        headless.Session.TryInterrupt();
        await headless.Session.CompleteTurnAsync(CancellationToken.None).WaitAsync(Wait);
    }

    private static bool SavedContains(HeadlessSession headless, string text) =>
        File.Exists(headless.Home.ConfigPath)
        && File.ReadAllText(headless.Home.ConfigPath).Contains(text, StringComparison.Ordinal);

    private static HarnessSettings TwoModels()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
            {
                ["model"] = new(200_000, serviceTiers: ["flex"]),
                ["other"] = new(200_000)
            },
            requiresApiKey: false);
        return new HarnessSettings(provider.Name, "model", ApprovalMode.Default, 0.8,
            HostDefaults.CreateCatalog().Overlay([provider]));
    }

    /// <summary>
    /// Holds the first request until released, then records whether the next
    /// request was built in Plan, which offers no write tool.
    /// </summary>
    private sealed class HoldingStreamingClient : IStreamingChatClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requests;

        public Task Started => _started.Task;

        public Task Second => _second.Task;

        public bool SecondPlan { get; private set; }

        public void Release() => _release.TrySetResult();

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
            EnumerateAsync(request, cancellationToken);

        public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StreamingTurn uses StreamAsync.");

        private async IAsyncEnumerable<ChatStreamEvent> EnumerateAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) == 1)
            {
                _started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            else
            {
                SecondPlan = !request.Tools.Any(static tool => tool.Name == "write");
                _second.TrySetResult();
            }

            yield return new ChatTextDelta(0, 0, ChatRole.Assistant, "done");
            yield return new ChatCandidateCompleted(0, FinishReason.Stop);
        }
    }
}
