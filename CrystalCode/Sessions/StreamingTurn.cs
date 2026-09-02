using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Compaction;

namespace CrystalCode.Sessions;

/// <summary>
/// Runs one user message through streaming model rounds and tool batches.
/// </summary>
public sealed class StreamingTurn
{
    private readonly IStreamingChatClient _client;
    private readonly IToolExecutor _executor;
    private readonly TurnLimits _limits;
    private readonly ITurnObserver? _observer;
    private readonly ReasoningOptions? _reasoning;
    private readonly Func<IReadOnlyList<ChatItem>, CancellationToken, Task<CompactionOutcome>>? _compactBeforeRound;
    private readonly SessionRetryOptions _retry;

    public StreamingTurn(
        IStreamingChatClient client,
        IToolExecutor executor,
        TurnLimits limits,
        ITurnObserver? observer = null,
        ReasoningOptions? reasoning = null,
        Func<IReadOnlyList<ChatItem>, CancellationToken, Task<CompactionOutcome>>? compactBeforeRound = null,
        SessionRetryOptions? retry = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(limits);
        _client = client;
        _executor = executor;
        _limits = limits;
        _observer = observer;
        _reasoning = reasoning;
        _compactBeforeRound = compactBeforeRound;
        _retry = retry ?? SessionRetryOptions.Default;
    }

    public async Task<TurnResult> RunAsync(
        IReadOnlyList<ChatItem> items,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var transcript = new List<ChatItem>(items);
        var modelCallCount = 0;
        var toolCallCount = 0;
        var usage = new UsageAccumulator();

        using var durationSource = new CancellationTokenSource(_limits.MaximumDuration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            durationSource.Token);

        try
        {
            while (true)
            {
                if (_compactBeforeRound is not null)
                {
                    var compacted = await _compactBeforeRound(transcript, linked.Token);
                    if (compacted.Kind == CompactionKind.Exhausted)
                    {
                        _observer?.OnModelRoundClosed();
                        return Create(
                            TurnStopReason.ContextOverflow,
                            modelCallCount,
                            toolCallCount,
                            usage,
                            transcript);
                    }

                    if (compacted.Kind == CompactionKind.Applied)
                    {
                        transcript.Clear();
                        transcript.AddRange(compacted.Transcript);
                    }
                }

                if (modelCallCount >= _limits.MaximumModelCalls)
                {
                    _observer?.OnModelRoundClosed();
                    return Create(
                        TurnStopReason.ModelCallLimitReached,
                        modelCallCount,
                        toolCallCount,
                        usage,
                        transcript);
                }

                modelCallCount++;
                var request = new ChatRequest(transcript, _executor.Definitions, _reasoning);
                var response = await StreamModelAsync(request, linked.Token);
                usage.Add(response.Usage);
                _observer?.OnUsageUpdated(response.Usage ?? usage.Last);

                var candidate = response.Candidates[0];
                transcript.AddRange(candidate.Items);

                var toolCalls = candidate.Items.OfType<ToolCall>().ToArray();
                if (toolCalls.Length == 0)
                {
                    _observer?.OnModelRoundClosed();
                    return Create(
                        TurnStopReason.Completed,
                        modelCallCount,
                        toolCallCount,
                        usage,
                        transcript);
                }

                if (toolCalls.Length > _limits.MaximumToolCalls - toolCallCount)
                {
                    _observer?.OnModelRoundClosed();
                    return Create(
                        TurnStopReason.ToolCallLimitReached,
                        modelCallCount,
                        toolCallCount,
                        usage,
                        transcript);
                }

                toolCallCount += toolCalls.Length;
                _observer?.OnModelRoundClosed();
                _observer?.OnToolCalls(toolCalls);
                var toolResults = await _executor.ExecuteAsync(toolCalls, linked.Token);
                transcript.AddRange(toolResults);
                _observer?.OnToolResults(toolResults);
                _observer?.OnUsageUpdated(response.Usage ?? usage.Last);
            }
        }
        catch (OperationCanceledException) when (durationSource.IsCancellationRequested)
        {
            _observer?.OnModelRoundClosed();
            return Create(
                TurnStopReason.DurationLimitReached,
                modelCallCount,
                toolCallCount,
                usage,
                transcript);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _observer?.OnModelRoundClosed();
            return Create(
                TurnStopReason.Interrupted,
                modelCallCount,
                toolCallCount,
                usage,
                transcript);
        }
    }

    private Task<ChatResponse> StreamModelAsync(
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        return SessionRetry.RunAsync(
            token => StreamOnceAsync(request, token),
            _retry,
            attempt => _observer?.OnRetry(attempt),
            cancellationToken);
    }

    private async Task<ChatResponse> StreamOnceAsync(
        ChatRequest request,
        CancellationToken cancellationToken)
    {
        var assembler = new ChatStreamAssembler();
        await foreach (var streamEvent in _client.StreamAsync(request, cancellationToken))
        {
            _observer?.OnStreamEvent(streamEvent);
            assembler.Apply(streamEvent);
        }

        return assembler.ToResponse();
    }

    private static TurnResult Create(
        TurnStopReason stopReason,
        int modelCallCount,
        int toolCallCount,
        UsageAccumulator usage,
        List<ChatItem> transcript)
    {
        ReconcilePendingToolCalls(transcript);
        return new(
            stopReason,
            modelCallCount,
            toolCallCount,
            usage.Last,
            transcript,
            usage.Build());
    }

    private static void ReconcilePendingToolCalls(List<ChatItem> transcript)
    {
        var completedToolCalls = new HashSet<string>(
            transcript.OfType<ToolResult>().Select(r => r.CallId),
            StringComparer.Ordinal);

        var missing = new List<ToolResult>();
        foreach (var item in transcript)
        {
            if (item is ToolCall call && !completedToolCalls.Contains(call.CallId))
            {
                missing.Add(new ToolResult(call.CallId, "Tool execution was interrupted by user.", ToolResultStatus.Failure));
                completedToolCalls.Add(call.CallId);
            }
        }

        transcript.AddRange(missing);
    }
}
