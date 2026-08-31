using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalHarness.Compaction;
using CrystalHarness.Sessions;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

public sealed class StreamingTurnTests
{
    [Fact]
    public async Task RunAsync_CompletesWhenModelReturnsText()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        var turn = CreateTurn(client);

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.Equal(1, result.ModelCallCount);
        Assert.Equal(0, result.ToolCallCount);
        Assert.Equal("done", Assert.IsType<ChatMessage>(result.Transcript[^1]).Text);
    }

    [Fact]
    public async Task RunAsync_ExecutesToolBatchThenCompletes()
    {
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "echo", "{}"),
            TextRound("ok"));
        var turn = CreateTurn(client);

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "echo")]);

        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.Equal(2, result.ModelCallCount);
        Assert.Equal(1, result.ToolCallCount);
        var toolResult = result.Transcript.OfType<ToolResult>().Single();
        Assert.Equal("c1", toolResult.CallId);
        Assert.Equal("echoed:{}", toolResult.Text);
        Assert.Equal("ok", Assert.IsType<ChatMessage>(result.Transcript[^1]).Text);
    }

    [Fact]
    public async Task RunAsync_StopsAtModelCallLimit()
    {
        var client = new ScriptedStreamingClient(ToolRound("c1", "echo", "{}"));
        var turn = CreateTurn(
            client,
            new TurnLimits(1, 8, TimeSpan.FromSeconds(5)));

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "loop")]);

        Assert.Equal(TurnStopReason.ModelCallLimitReached, result.StopReason);
        Assert.Equal(1, result.ModelCallCount);
        Assert.Equal(1, result.ToolCallCount);
    }

    [Fact]
    public async Task RunAsync_InterruptedToolCall_ReconcilesCancelledToolResults()
    {
        using var cts = new CancellationTokenSource();
        var client = new ScriptedStreamingClient(ToolRound("c_cancel", "cancel_tool", "{}"));
        var cancellingTool = new CancellingTool(() => cts.Cancel());
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([cancellingTool]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)));

        var result = await turn.RunAsync(
            [new ChatMessage(ChatRole.User, "test")],
            cts.Token);

        Assert.Equal(TurnStopReason.Interrupted, result.StopReason);
        var call = result.Transcript.OfType<ToolCall>().Single();
        var toolResult = result.Transcript.OfType<ToolResult>().Single();
        Assert.Equal(call.CallId, toolResult.CallId);
        Assert.Equal(ToolResultStatus.Failure, toolResult.Status);
        Assert.Contains("interrupted", toolResult.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ReportsUsageUpdatesToObserver()
    {
        var observer = new TestObserver();
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "echo", "{}", new TokenUsage(10, 5, 2)),
            TextRound("ok", new TokenUsage(20, 10, 4)));
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            observer);

        var result = await turn.RunAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.True(observer.UsageUpdates.Count >= 2);
        Assert.Equal(30, observer.UsageUpdates[^1]?.InputTokenCount);
        Assert.Equal(15, observer.UsageUpdates[^1]?.OutputTokenCount);
        var calls = Assert.Single(observer.ToolCallBatches);
        Assert.Equal("c1", Assert.Single(calls).CallId);
        Assert.Equal("echo", calls[0].Name);
        var results = Assert.Single(observer.ToolResultBatches);
        Assert.Equal("c1", Assert.Single(results).CallId);
        Assert.True(observer.ToolCallsBeforeResults);
    }

    [Fact]
    public async Task RunAsync_DoesNotNotifyToolCallsWhenTheRoundHasNone()
    {
        var observer = new TestObserver();
        var client = new ScriptedStreamingClient(TextRound("done"));
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            observer);

        await turn.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Empty(observer.ToolCallBatches);
        Assert.Empty(observer.ToolResultBatches);
    }

    [Fact]
    public async Task RunAsync_PassesResolvedReasoningToTheClient()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        var reasoning = new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.High);
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            reasoning: reasoning);

        await turn.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Same(reasoning, client.LastRequest?.Reasoning);
    }

    [Fact]
    public async Task RunAsync_StopsWhenCompactionIsExhausted()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            compactBeforeRound: (_, _) => Task.FromResult(
                new CompactionOutcome([], CompactionKind.Exhausted)));

        var result = await turn.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(TurnStopReason.ContextOverflow, result.StopReason);
        Assert.Equal(0, result.ModelCallCount);
        Assert.Null(client.LastRequest);
    }

    private static StreamingTurn CreateTurn(
        IStreamingChatClient client,
        TurnLimits? limits = null) =>
        new(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            limits ?? new TurnLimits(8, 8, TimeSpan.FromSeconds(5)));

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

    private static ChatStreamEvent[] ToolRound(string callId, string name, string arguments, TokenUsage? usage = null)
    {
        var events = new List<ChatStreamEvent>
        {
            new ChatToolCallDelta(0, 0, callId, name, arguments),
            new ChatCandidateCompleted(0, FinishReason.ToolCalls)
        };
        if (usage is not null)
        {
            events.Add(new ChatUsageReceived(usage));
        }

        return [.. events];
    }

    private sealed class CancellingTool : ITool
    {
        private readonly Action _onExecute;

        public CancellingTool(Action onExecute)
        {
            _onExecute = onExecute;
            Definition = new ToolDefinition(
                "cancel_tool",
                ToolSchema.Parse("""{"type":"object","properties":{}}"""),
                "Cancels execution.");
        }

        public ToolDefinition Definition { get; }

        public ValueTask<ToolOutput> InvokeAsync(
            ToolCall call,
            CancellationToken cancellationToken = default)
        {
            _onExecute();
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class TestObserver : ITurnObserver
    {
        public List<TokenUsage?> UsageUpdates { get; } = [];

        public List<IReadOnlyList<ToolCall>> ToolCallBatches { get; } = [];

        public List<IReadOnlyList<ToolResult>> ToolResultBatches { get; } = [];

        public bool ToolCallsBeforeResults { get; private set; }

        public void OnStreamEvent(ChatStreamEvent streamEvent) { }

        public void OnModelRoundClosed() { }

        public void OnToolCalls(IReadOnlyList<ToolCall> calls)
        {
            ToolCallsBeforeResults = ToolResultBatches.Count == 0;
            ToolCallBatches.Add(calls);
        }

        public void OnToolResults(IReadOnlyList<ToolResult> results)
        {
            ToolResultBatches.Add(results);
        }

        public void OnUsageUpdated(TokenUsage? usage) => UsageUpdates.Add(usage);
    }
}
