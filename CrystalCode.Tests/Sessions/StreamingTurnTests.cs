using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Compaction;
using CrystalCode.Providers.DeepSeek;
using CrystalCode.Sessions;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Sessions;

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
        Assert.Equal(20, observer.UsageUpdates[^1].Context?.InputTokenCount);
        Assert.Equal(10, observer.UsageUpdates[^1].Context?.OutputTokenCount);
        Assert.Equal(30, observer.UsageUpdates[^1].TurnCumulative?.InputTokenCount);
        Assert.Equal(20, result.Usage?.InputTokenCount);
        Assert.Equal(10, result.Usage?.OutputTokenCount);
        Assert.Equal(30, result.AccumulatedUsage?.InputTokenCount);
        Assert.Equal(15, result.AccumulatedUsage?.OutputTokenCount);
        Assert.Equal(6, result.AccumulatedUsage?.ReasoningTokenCount);
        var calls = Assert.Single(observer.ToolCallBatches);
        Assert.Equal("c1", Assert.Single(calls).CallId);
        Assert.Equal("echo", calls[0].Name);
        var results = Assert.Single(observer.ToolResultBatches);
        Assert.Equal("c1", Assert.Single(results).CallId);
        Assert.True(observer.ToolCallsBeforeResults);
    }

    [Fact]
    public async Task RunAsync_UpdatesContextEstimateAfterToolResults()
    {
        var observer = new TestObserver();
        var client = new ScriptedStreamingClient(
            ToolRound("c1", "echo", """{"message":"hi"}""", new TokenUsage(10, 5)),
            TextRound("ok", new TokenUsage(20, 10)));
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            observer);

        await turn.RunAsync([new ChatMessage(ChatRole.User, "hi")]);

        var afterTools = observer.UsageUpdates
            .Last(update => update.Context?.OutputTokenCount == 0
                && update.Context.InputTokenCount > 10);
        Assert.Equal(10, afterTools.TurnCumulative?.InputTokenCount);
        Assert.Equal(5, afterTools.TurnCumulative?.OutputTokenCount);
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

    [Fact]
    public async Task RunAsync_RetriesRetryableModelFailureThenCompletes()
    {
        var observer = new TestObserver();
        var client = new FlakyStreamingClient(
            new DeepSeekException("slow down", statusCode: 429, retryAfter: TimeSpan.FromSeconds(8)),
            TextRound("done"));
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            observer,
            retry: InstantRetry());

        var result = await turn.RunAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal(TurnStopReason.Completed, result.StopReason);
        Assert.Equal(1, result.ModelCallCount);
        Assert.Equal(2, client.RequestCount);
        var retry = Assert.Single(observer.Retries);
        Assert.Equal(1, retry.Attempt);
        Assert.Equal(TimeSpan.FromSeconds(8), retry.Delay);
        Assert.Equal("done", Assert.IsType<ChatMessage>(result.Transcript[^1]).Text);
    }

    [Fact]
    public async Task RunAsync_DoesNotRetryClientErrors()
    {
        var client = new FlakyStreamingClient(
            new DeepSeekException("bad model", statusCode: 400, errorCode: "invalid_request"));
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            retry: InstantRetry());

        var exception = await Assert.ThrowsAsync<DeepSeekException>(
            () => turn.RunAsync([new ChatMessage(ChatRole.User, "hello")]));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public async Task RunAsync_GivesUpAfterMaximumRetries()
    {
        var client = new FlakyStreamingClient(
            new DeepSeekException("slow down", statusCode: 429));
        var turn = new StreamingTurn(
            client,
            new ToolExecutor(
                new ToolCatalog([new EchoTool()]),
                new ToolExecutionOptions(ToolExecutionMode.Serial, 1)),
            new TurnLimits(8, 8, TimeSpan.FromSeconds(5)),
            retry: InstantRetry(maximumRetries: 2));

        await Assert.ThrowsAsync<DeepSeekException>(
            () => turn.RunAsync([new ChatMessage(ChatRole.User, "hello")]));

        Assert.Equal(3, client.RequestCount);
    }

    private static SessionRetryOptions InstantRetry(int maximumRetries = 5) =>
        new(
            maximumRetries,
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            () => 0);

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
        public List<(TokenUsage? Context, TokenUsage? TurnCumulative)> UsageUpdates { get; } = [];

        public List<IReadOnlyList<ToolCall>> ToolCallBatches { get; } = [];

        public List<IReadOnlyList<ToolResult>> ToolResultBatches { get; } = [];

        public bool ToolCallsBeforeResults { get; private set; }

        public List<SessionRetryAttempt> Retries { get; } = [];

        public void OnStreamEvent(ChatStreamEvent streamEvent) { }

        public void OnRetry(SessionRetryAttempt attempt) => Retries.Add(attempt);

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

        public void OnUsageUpdated(TokenUsage? contextUsage, TokenUsage? turnCumulativeUsage = null) =>
            UsageUpdates.Add((contextUsage, turnCumulativeUsage));
    }

    private sealed class FlakyStreamingClient : IStreamingChatClient
    {
        private readonly Exception _failure;
        private readonly IReadOnlyList<ChatStreamEvent>? _success;

        public FlakyStreamingClient(Exception failure, IReadOnlyList<ChatStreamEvent>? success = null)
        {
            _failure = failure;
            _success = success;
        }

        public int RequestCount { get; private set; }

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            if (_success is null || RequestCount == 1)
            {
                throw _failure;
            }

            return EnumerateAsync(_success, cancellationToken);
        }

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("StreamingTurn uses StreamAsync.");
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
}
