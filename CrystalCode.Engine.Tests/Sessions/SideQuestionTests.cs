using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SideQuestionTests
{
    [Fact]
    public async Task Btw_AnswersFromTheTranscriptWithoutSavingOrTools()
    {
        var client = new ScriptedStreamingClient(
            TextRound("Because the retry waits.", new TokenUsage(12, 4)));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        headless.Observer.Clear();

        var quit = await headless.Session.SubmitAsync("/btw why retry", CancellationToken.None);
        await headless.Session.SideQuestionTask;

        Assert.False(quit);
        Assert.DoesNotContain(
            headless.Observer.Events,
            static e => e is TurnStarted or UserMessageSent or UsageChanged);
        var finished = headless.Observer.Events.OfType<SideQuestionSnapshot>().Last();
        Assert.False(finished.Announce);
        Assert.False(finished.Running);
        Assert.Equal("why retry", finished.Exchanges[0].Question);
        Assert.Equal("Because the retry waits.", finished.Exchanges[0].Answer);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/btw", CancellationToken.None);
        var reopened = headless.Observer.Events.OfType<SideQuestionSnapshot>().Single();
        Assert.True(reopened.Announce);
        Assert.Equal("Because the retry waits.", reopened.Exchanges[0].Answer);
        Assert.NotNull(client.LastRequest);
        Assert.Empty(client.LastRequest.Tools);
        Assert.Equal(ChatRole.System, Assert.IsType<ChatMessage>(client.LastRequest.Items[0]).Role);
        var asked = Assert.IsType<ChatMessage>(client.LastRequest.Items[^1]);
        Assert.Equal(ChatRole.User, asked.Role);
        Assert.Contains("why retry", asked.Text, StringComparison.Ordinal);
        Assert.Contains("Do not call tools.", asked.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Btw_ShowsThinkingBeforeTheAnswerAndKeepsTheReasoningOut()
    {
        var client = new ScriptedStreamingClient(
        [
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "plan"),
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, " more"),
            new ChatTextDelta(0, 1, ChatRole.Assistant, "Because."),
            new ChatCandidateCompleted(0, FinishReason.Stop)
        ]);
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/btw why", CancellationToken.None);
        await headless.Session.SideQuestionTask;

        var snapshots = headless.Observer.Events.OfType<SideQuestionSnapshot>().ToArray();
        Assert.Single(snapshots, static snapshot =>
            snapshot.Thinking
            && snapshot.Running
            && snapshot.PendingQuestion == "why"
            && snapshot.LiveAnswer.Length == 0);
        Assert.Contains(snapshots, static snapshot => snapshot.LiveAnswer == "Because.");
        var finished = snapshots[^1];
        Assert.Equal("Because.", finished.Exchanges[0].Answer);
        Assert.False(finished.Thinking);
        Assert.DoesNotContain(headless.Observer.Events, static item => item is StreamReceived);
        Assert.All(snapshots, static snapshot =>
        {
            Assert.DoesNotContain("plan", snapshot.LiveAnswer, StringComparison.Ordinal);
            Assert.DoesNotContain(
                snapshot.Exchanges,
                static exchange => exchange.Answer.Contains("plan", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Btw_KeepsTextAndIgnoresAToolCall()
    {
        var client = new ScriptedStreamingClient(
        [
            new ChatTextDelta(0, 0, ChatRole.Assistant, "Because."),
            new ChatToolCallDelta(0, 1, "c1", "read", "{}"),
            new ChatCandidateCompleted(0, FinishReason.ToolCalls)
        ]);
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/side why", CancellationToken.None);
        await headless.Session.SideQuestionTask;

        var finished = headless.Observer.Events.OfType<SideQuestionSnapshot>().Last();
        Assert.Equal("Because.", finished.Exchanges[0].Answer);
        Assert.Equal(0, headless.Approvals.Count);
    }

    [Fact]
    public async Task Btw_ReportsAToolOnlyAnswerWithoutRunningIt()
    {
        var client = new ScriptedStreamingClient(ToolRound("c1", "read", "{}"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/btw look", CancellationToken.None);
        await headless.Session.SideQuestionTask;

        var finished = headless.Observer.Events.OfType<SideQuestionSnapshot>().Last();
        Assert.Empty(finished.Exchanges);
        Assert.Equal("look", finished.PendingQuestion);
        Assert.Equal(SideQuestion.CannotUseTools, finished.Failure);
        Assert.Equal(0, headless.Approvals.Count);
    }

    [Fact]
    public async Task Btw_KeepsTheQuestionWhenTheModelFails()
    {
        using var headless = new HeadlessSession(new ThrowingStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/btw why", CancellationToken.None);
        await headless.Session.SideQuestionTask;

        var finished = headless.Observer.Events.OfType<SideQuestionSnapshot>().Last();
        Assert.Equal("why", finished.PendingQuestion);
        Assert.Contains("The model request failed.", finished.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(headless.Observer.Events, static e => e is TurnStarted);
    }

    [Fact]
    public async Task Btw_ReplaysEarlierSideAnswersAndDropsThemOnClear()
    {
        var client = new ScriptedStreamingClient(
            TextRound("one"),
            TextRound("two"));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/btw first", CancellationToken.None);
        await headless.Session.SideQuestionTask;
        await headless.Session.SubmitAsync("/btw second", CancellationToken.None);
        await headless.Session.SideQuestionTask;

        Assert.Contains(
            client.LastRequest!.Items,
            static item => item is ChatMessage message
                && message.Role == ChatRole.User
                && message.Text == "first");
        var asked = Assert.IsType<ChatMessage>(client.LastRequest.Items[^1]);
        Assert.Contains("second", asked.Text, StringComparison.Ordinal);

        headless.Observer.Clear();
        await headless.Session.SubmitAsync("/clear", CancellationToken.None);
        await headless.Session.SubmitAsync("/btw", CancellationToken.None);

        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            static error => error.Text == SideQuestion.NoneToShow);
    }

    [Fact]
    public async Task Btw_KeepsTheNewestExchangesOnly()
    {
        var rounds = Enumerable.Range(0, SideQuestion.MemoryLimit + 1)
            .Select(static index => TextRound("a" + index))
            .ToArray();
        using var headless = new HeadlessSession(new ScriptedStreamingClient(rounds));
        await headless.Session.StartAsync(CancellationToken.None);

        for (var index = 0; index <= SideQuestion.MemoryLimit; index++)
        {
            await headless.Session.SubmitAsync("/btw q" + index, CancellationToken.None);
            await headless.Session.SideQuestionTask;
        }

        var saved = headless.Observer.Events.OfType<SideQuestionSnapshot>().Last().Exchanges;
        Assert.Equal(SideQuestion.MemoryLimit, saved.Count);
        Assert.Equal("q1", saved[0].Question);
        Assert.Equal("q" + SideQuestion.MemoryLimit, saved[^1].Question);
    }

    [Fact]
    public async Task Btw_DoesNotQueueWhileATurnIsRunning()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("work", CancellationToken.None);
        await client.Started;

        var quit = await headless.Session.SubmitAsync("/btw what", CancellationToken.None);

        Assert.False(quit);
        Assert.Contains(
            headless.Observer.Events.OfType<SideQuestionSnapshot>(),
            static snapshot => snapshot.Running && snapshot.PendingQuestion == "what");
        Assert.DoesNotContain(
            headless.Observer.Events.OfType<QueueChanged>(),
            static queue => queue.Items.Contains("what"));
        Assert.Single(headless.Observer.Events.OfType<TurnStarted>());
        Assert.DoesNotContain(
            headless.Observer.Events.OfType<UserMessageSent>(),
            static user => user.Text.Contains("what", StringComparison.Ordinal));

        headless.Session.TryCancelSideQuestion();
        headless.Session.TryInterrupt();
        await headless.Session.TurnTask!;
        await headless.Session.SideQuestionTask;
        await headless.Session.CompleteTurnAsync(CancellationToken.None);

        var finished = headless.Observer.Events.OfType<SideQuestionSnapshot>().Last();
        Assert.Empty(finished.Exchanges);
        Assert.Equal(SideQuestion.Cancelled, finished.Failure);
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

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments) =>
    [
        new ChatToolCallDelta(0, 0, callId, name, arguments),
        new ChatCandidateCompleted(0, FinishReason.ToolCalls)
    ];

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
