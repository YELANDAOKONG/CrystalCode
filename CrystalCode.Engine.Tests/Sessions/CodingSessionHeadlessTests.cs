using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Runs a real session with no terminal. A second front end can drive the
/// engine only if everything it needs arrives as events.
/// </summary>
public sealed class CodingSessionHeadlessTests
{
    [Fact]
    public async Task Turn_PublishesOrderedEventsAndFinishesCompleted()
    {
        var client = new ScriptedStreamingClient(
            TextRound("Hello there.", new TokenUsage(10, 5)));
        using var headless = new HeadlessSession(client);

        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("hi");

        var events = headless.Observer.Events;
        var started = IndexOf<SessionStarted>(events);
        var sent = IndexOf<UserMessageSent>(events);
        var begun = IndexOf<TurnStarted>(events);
        var streamed = IndexOf<StreamReceived>(events);
        var finished = IndexOf<TurnFinished>(events);
        Assert.True(started < sent);
        Assert.True(sent < begun);
        Assert.True(begun < streamed);
        Assert.True(streamed < finished);

        Assert.Equal("hi", ((UserMessageSent)events[sent]).Text);
        var text = string.Concat(
            events.OfType<StreamReceived>()
                .Select(static received => received.StreamEvent)
                .OfType<ChatTextDelta>()
                .Select(static delta => delta.Text));
        Assert.Equal("Hello there.", text);

        var result = ((TurnFinished)events[finished]).Result;
        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.Equal(1, result.ModelCallCount);
        Assert.False(headless.Session.TurnActive);
    }

    [Fact]
    public async Task Turn_RunsWorkspaceToolWithoutAskingForApproval()
    {
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "read", """{"path":"notes.txt"}"""),
            TextRound("It says alpha."));
        using var headless = new HeadlessSession(client);
        await File.WriteAllTextAsync(
            Path.Combine(headless.WorkspacePath, "notes.txt"),
            "alpha");

        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("read the notes");

        var events = headless.Observer.Events;
        var issued = events.OfType<ToolCallsIssued>().Single();
        Assert.Equal("read", Assert.Single(issued.Calls).Name);
        var received = events.OfType<ToolResultsReceived>().Single();
        Assert.Contains("alpha", Assert.Single(received.Results).Text, StringComparison.Ordinal);
        Assert.True(IndexOf<ToolCallsIssued>(events) < IndexOf<ToolResultsReceived>(events));

        var result = events.OfType<TurnFinished>().Single().Result;
        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.Equal(2, result.ModelCallCount);
        Assert.Equal(1, result.ToolCallCount);
        Assert.Equal(0, headless.Approvals.Count);
    }

    [Fact]
    public async Task SecondTurn_ReportsCumulativeUsageOnTopOfTheFirst()
    {
        var client = new ScriptedStreamingClient(
            TextRound("one", new TokenUsage(10, 5)),
            TextRound("two", new TokenUsage(40, 8)));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("first");
        headless.Observer.Clear();

        await headless.RunTurnAsync("second");

        var events = headless.Observer.Events;
        var interim = events.OfType<UsageChanged>().Where(static usage => usage.Interim).ToArray();
        Assert.NotEmpty(interim);
        foreach (var usage in interim)
        {
            Assert.Equal(40, usage.Usage?.InputTokenCount);
            Assert.Equal(50, usage.CumulativeUsage?.InputTokenCount);
            Assert.Equal(13, usage.CumulativeUsage?.OutputTokenCount);
        }

        var finished = events.OfType<TurnFinished>().Single();
        Assert.Equal(40, finished.Usage?.InputTokenCount);
        Assert.Equal(50, finished.CumulativeUsage?.InputTokenCount);
        Assert.Equal(13, finished.CumulativeUsage?.OutputTokenCount);
        Assert.Equal(200_000, finished.ContextWindow);
    }

    [Fact]
    public async Task TryInterrupt_StopsARunningTurnAsInterrupted()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        var quit = await headless.Session.SubmitAsync("long task", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(quit);
        Assert.True(headless.Session.TurnActive);
        Assert.True(headless.Session.TryInterrupt());
        await headless.Session.CompleteTurnAsync(CancellationToken.None);

        var finished = headless.Observer.Events.OfType<TurnFinished>().Single();
        Assert.Equal(TurnStopReason.Interrupted, finished.Result.StopReason);
        Assert.False(headless.Session.TurnActive);
        Assert.False(headless.Session.TryInterrupt());
    }

    [Fact]
    public async Task Turn_ProviderFailurePublishesTheErrorAndFinishes()
    {
        using var headless = new HeadlessSession(new ThrowingStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.RunTurnAsync("hello");

        var error = headless.Observer.Events.OfType<ErrorWritten>().Single();
        Assert.Equal("The model request failed.", error.Text);
        var finished = headless.Observer.Events.OfType<TurnFinished>().Single();
        Assert.Equal(TurnStopReason.Failed, finished.Result.StopReason);
        Assert.Contains(
            finished.Result.Transcript,
            static item => item is ChatMessage message
                && message.Role == ChatRole.User
                && message.Text == "hello");
        Assert.False(headless.Session.TurnActive);
    }

    [Theory]
    [InlineData("/quit")]
    [InlineData("/exit")]
    public async Task Submit_QuitCommandAsksTheFrontEndToExit(string input)
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        var quit = await headless.Session.SubmitAsync(input, CancellationToken.None);

        Assert.True(quit);
        Assert.False(headless.Session.TurnActive);
        Assert.DoesNotContain(headless.Observer.Events, static e => e is TurnStarted);
    }

    [Fact]
    public async Task StatsCommand_PrintsTokenAndToolSummary()
    {
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "read", """{"path":"notes.txt"}"""),
            TextRound("It says alpha."));
        using var headless = new HeadlessSession(client);
        await File.WriteAllTextAsync(Path.Combine(headless.WorkspacePath, "notes.txt"), "alpha");
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("read notes");
        headless.Observer.Clear();

        var quit = await headless.Session.SubmitAsync("/stats", CancellationToken.None);

        Assert.False(quit);
        var note = headless.Observer.Events.OfType<NoteWritten>().Single().Text;
        Assert.Contains("Stats · This workspace", note, StringComparison.Ordinal);
        Assert.Contains("Tokens", note, StringComparison.Ordinal);
        Assert.Contains("Top tools", note, StringComparison.Ordinal);
        Assert.Contains("read", note, StringComparison.Ordinal);
    }

    private static int IndexOf<TEvent>(IReadOnlyList<SessionEvent> events)
        where TEvent : SessionEvent
    {
        for (var index = 0; index < events.Count; index++)
        {
            if (events[index] is TEvent)
            {
                return index;
            }
        }

        throw new Xunit.Sdk.XunitException($"No {typeof(TEvent).Name} was published.");
    }

    private static ChatStreamEvent[] TextRound(string text, TokenUsage? usage = null)
    {
        var events = new List<ChatStreamEvent>
        {
            new ChatTextDelta(0, 0, ChatRole.Assistant, text),
            new ChatCandidateCompleted(0, FinishReason.Stop)
        };
        if (usage is not null)
        {
            events.Add(new ChatUsageReceived(usage));
        }

        return [.. events];
    }

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments)
    {
        return
        [
            new ChatToolCallDelta(0, 0, callId, name, arguments),
            new ChatCandidateCompleted(0, FinishReason.ToolCalls)
        ];
    }

    private sealed class ThrowingStreamingClient : IStreamingChatClient
    {
        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The model request failed.");

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("StreamingTurn uses StreamAsync.");
    }
}
