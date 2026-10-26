using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionStateTests
{
    [Fact]
    public async Task Snapshot_MatchesTheArchiveAfterATurnWithTools()
    {
        var client = new ScriptedStreamingClient(
            [
                new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "thinking"),
                new ChatTextDelta(0, 1, ChatRole.Assistant, "Reading."),
                JsonChatToolCallDelta(0, 2, "call-1", "todoread", "{}"),
                new ChatCandidateCompleted(0, FinishReason.ToolCalls)
            ],
            TextRound("Done."));
        using var headless = new HeadlessSession(client);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("look");

        var state = Replay(headless.Observer.Events);
        var snapshot = state.Snapshot();

        Assert.False(snapshot.TurnActive);
        Assert.Null(snapshot.Live);
        Assert.NotNull(snapshot.LastTurn);
        Assert.Collection(
            snapshot.Conversation,
            item => Assert.Equal("look", Assert.IsType<ChatMessage>(item).Text),
            item => Assert.IsType<ChatReasoningItem>(item),
            item => Assert.Equal("Reading.", Assert.IsType<ChatMessage>(item).Text),
            item => Assert.Equal("call-1", Assert.IsType<ToolCall>(item).CallId),
            item => Assert.Equal("call-1", Assert.IsType<ToolResult>(item).CallId),
            item => Assert.Equal("Done.", Assert.IsType<ChatMessage>(item).Text));
        Assert.NotNull(snapshot.Chrome);
        Assert.NotNull(snapshot.Preferences);
        Assert.NotEmpty(snapshot.Commands);
    }

    [Fact]
    public void Snapshot_HoldsTheRoundStillStreaming()
    {
        var state = new SessionState();
        state.Apply(new UserMessageSent("hi"));
        state.Apply(new TurnStarted());
        state.Apply(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "Hel")));
        state.Apply(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "lo")));
        state.Apply(new StreamReceived(JsonChatToolCallDelta(0, 1, "c", "read", "{\"pa")));

        var live = state.Snapshot().Live;

        Assert.NotNull(live);
        Assert.Equal("Hello", live.Text);
        Assert.Equal("{\"pa", Assert.Single(live.ToolCalls).Arguments);
        Assert.True(state.Snapshot().TurnActive);
    }

    [Fact]
    public void Retry_DiscardsTheFailedAttempt()
    {
        var state = new SessionState();
        state.Apply(new TurnStarted());
        state.Apply(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "partial")));
        state.Apply(new RetryScheduled(new SessionRetryAttempt(1, "busy", TimeSpan.FromSeconds(1))));

        var live = state.Snapshot().Live!;

        Assert.Equal(string.Empty, live.Text);
        Assert.Equal(1, live.Retries);
    }

    [Fact]
    public void Recall_RemovesTheMessage()
    {
        var state = new SessionState();
        state.Apply(new UserMessageSent("oops"));
        state.Apply(new UserMessageRecalled("oops"));

        Assert.Empty(state.Snapshot().Conversation);
    }

    [Fact]
    public void Children_FoldLiveUsageAndTodos()
    {
        var state = new SessionState();
        var child = new ChildSessionDocument { Id = "child-1", Status = "running", Label = "look" };
        state.Apply(new ChildSessionChanged(child, Guid.NewGuid()));
        state.Apply(new ChildEventReceived("child-1", Guid.NewGuid(),
            new UsageChanged(new TokenUsage(3, 2), new TokenUsage(30, 20), 1000, Interim: true)));
        state.Apply(new ChildEventReceived("child-1", Guid.NewGuid(),
            new TodosChanged([new TodoItem("1", "step", TodoStatus.InProgress)])));
        state.Apply(new ChildSessionChanged(child, Guid.NewGuid()));

        var folded = Assert.Single(state.Snapshot().Children);

        Assert.Equal(30, folded.CumulativeUsage!.InputTokenCount);
        Assert.Equal("in_progress", Assert.Single(folded.Todos).Status);
    }

    [Fact]
    public void Clear_ResetsTheConversation()
    {
        var state = new SessionState();
        state.Apply(new UserMessageSent("hi"));
        state.Apply(new ConversationCleared());

        Assert.Empty(state.Snapshot().Conversation);
    }

    private static SessionState Replay(IEnumerable<SessionEvent> events)
    {
        var state = new SessionState();
        foreach (var value in events)
        {
            state.Apply(value);
        }

        return state;
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
