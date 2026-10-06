using Crystal;
using Crystal.Chat;
using Crystal.Multimodal.Chat;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Engine.Compaction;

namespace CrystalCode.Engine.Sessions;

/// <summary>Runs an image-capable user turn while preserving the text transcript.</summary>
public sealed class MultimodalStreamingTurn
{
    private readonly IStreamingMultimodalChatClient _client;
    private readonly IMultimodalToolExecutor _executor;
    private readonly TurnLimits _limits;
    private readonly ITurnObserver? _observer;
    private readonly ReasoningOptions? _reasoning;
    private readonly Func<IReadOnlyList<ChatItem>, CancellationToken, Task<CompactionOutcome>>?
        _compactBeforeRound;
    private readonly SessionRetryOptions _retry;
    private readonly Func<int> _reserveImageNumber;
    private readonly Action<ImageAttachment> _addImage;
    private readonly Func<IDictionary<int, ImageAttachment>> _imageSnapshot;

    public MultimodalStreamingTurn(
        IStreamingMultimodalChatClient client,
        IMultimodalToolExecutor executor,
        TurnLimits limits,
        IDictionary<int, ImageAttachment> images,
        ITurnObserver? observer = null,
        ReasoningOptions? reasoning = null,
        Func<IReadOnlyList<ChatItem>, CancellationToken, Task<CompactionOutcome>>?
            compactBeforeRound = null,
        SessionRetryOptions? retry = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(images);
        _client = client;
        _executor = executor;
        _limits = limits;
        var imageGate = new object();
        var nextImageNumber = images.Keys.DefaultIfEmpty(0).Max() + 1;
        _reserveImageNumber = () =>
        {
            lock (imageGate)
            {
                return nextImageNumber++;
            }
        };
        _addImage = image =>
        {
            lock (imageGate)
            {
                images.Add(image.Number, image);
            }
        };
        _imageSnapshot = () =>
        {
            lock (imageGate)
            {
                return new Dictionary<int, ImageAttachment>(images);
            }
        };
        _observer = observer;
        _reasoning = reasoning;
        _compactBeforeRound = compactBeforeRound;
        _retry = retry ?? SessionRetryOptions.Default;
    }

    internal MultimodalStreamingTurn(
        IStreamingMultimodalChatClient client,
        IMultimodalToolExecutor executor,
        TurnLimits limits,
        IDictionary<int, ImageAttachment> images,
        ITurnObserver? observer,
        ReasoningOptions? reasoning,
        Func<IReadOnlyList<ChatItem>, CancellationToken, Task<CompactionOutcome>>?
            compactBeforeRound,
        SessionRetryOptions? retry,
        Func<int> reserveImageNumber,
        Action<ImageAttachment> addImage,
        Func<IDictionary<int, ImageAttachment>> imageSnapshot)
        : this(client, executor, limits, images, observer, reasoning, compactBeforeRound, retry)
    {
        _reserveImageNumber = reserveImageNumber;
        _addImage = addImage;
        _imageSnapshot = imageSnapshot;
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

        using var durationSource = _limits.MaximumDuration is TimeSpan duration
            ? new CancellationTokenSource(duration)
            : new CancellationTokenSource();
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

                if (_limits.MaximumModelCalls is int maximumModelCalls
                    && modelCallCount >= maximumModelCalls)
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
                var request = new MultimodalChatRequest(
                    MultimodalTranscript.Convert(transcript, _imageSnapshot()),
                    _executor.Definitions,
                    _reasoning);
                var response = await StreamModelAsync(request, usage, linked.Token);
                usage.Add(response.Usage);
                _observer?.OnUsageUpdated(response.Usage ?? usage.Last, usage.Build());

                var candidate = response.Candidates[0];
                if (CandidateRound.TryStop(candidate, transcript, out var stopReason, out var fault))
                {
                    if (fault is not null)
                    {
                        _observer?.OnFault(fault);
                    }

                    _observer?.OnModelRoundClosed();
                    return Create(
                        stopReason,
                        modelCallCount,
                        toolCallCount,
                        usage,
                        transcript);
                }

                var toolCalls = candidate.Items.OfType<ToolCall>().ToArray();
                if (toolCalls.Length == 0)
                {
                    transcript.AddRange(candidate.Items);
                    _observer?.OnModelRoundClosed();
                    return Create(
                        TurnStopReason.Completed,
                        modelCallCount,
                        toolCallCount,
                        usage,
                        transcript);
                }

                if (_limits.MaximumToolCalls is int maximumToolCalls
                    && toolCalls.Length > maximumToolCalls - toolCallCount)
                {
                    CandidateRound.AppendWithoutToolCalls(transcript, candidate.Items);
                    _observer?.OnModelRoundClosed();
                    return Create(
                        TurnStopReason.ToolCallLimitReached,
                        modelCallCount,
                        toolCallCount,
                        usage,
                        transcript);
                }

                transcript.AddRange(candidate.Items);

                toolCallCount += toolCalls.Length;
                _observer?.OnModelRoundClosed();
                _observer?.OnToolCalls(toolCalls);
                var multimodalCalls = toolCalls.Select(static call =>
                    new MultimodalToolCall(
                        call.CallId,
                        call.Name,
                        call.Arguments));
                var multimodalResults = await _executor.ExecuteAsync(
                    multimodalCalls,
                    linked.Token);
                var toolResults = ConvertToolResults(multimodalResults);
                transcript.AddRange(toolResults);
                _observer?.OnToolResults(toolResults);
                _observer?.OnUsageUpdated(
                    new TokenUsage(TokenEstimator.Items(transcript), 0),
                    usage.Build());
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _observer?.OnModelRoundClosed();
            _observer?.OnFault(exception.Message);
            return Create(
                TurnStopReason.Failed,
                modelCallCount,
                toolCallCount,
                usage,
                transcript);
        }
    }

    private Task<ChatResponse> StreamModelAsync(
        MultimodalChatRequest request,
        UsageAccumulator usage,
        CancellationToken cancellationToken) =>
        SessionRetry.RunAsync(
            token => StreamOnceAsync(request, usage, token),
            _retry,
            attempt => _observer?.OnRetry(attempt),
            cancellationToken);

    private async Task<ChatResponse> StreamOnceAsync(
        MultimodalChatRequest request,
        UsageAccumulator usage,
        CancellationToken cancellationToken)
    {
        var assembler = new ChatStreamAssembler();
        await foreach (var streamEvent in _client.StreamAsync(request, cancellationToken))
        {
            var converted = MultimodalStreamAdapter.Convert(streamEvent);
            if (converted is null)
            {
                continue;
            }

            _observer?.OnStreamEvent(converted);
            if (converted is ChatUsageReceived { Usage: not null } usageReceived)
            {
                _observer?.OnUsageUpdated(
                    usageReceived.Usage,
                    usage.Preview(usageReceived.Usage));
            }

            assembler.Apply(converted);
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
        return new TurnResult(
            stopReason,
            modelCallCount,
            toolCallCount,
            usage.Last,
            transcript,
            usage.Build());
    }

    private IReadOnlyList<ToolResult> ConvertToolResults(
        IReadOnlyList<MultimodalToolResult> results)
    {
        var converted = new List<ToolResult>(results.Count);
        foreach (var result in results)
        {
            var blocks = new List<string>();
            var unsupported = false;
            foreach (var content in result.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        blocks.Add(text.Text);
                        break;
                    case ImageContent image:
                        try
                        {
                            var attachment = CreateAttachment(image.Image);
                            _addImage(attachment);
                            blocks.Add(attachment.TrustedMarker);
                        }
                        catch (Exception exception) when (exception is ArgumentException
                            or NotSupportedException)
                        {
                            blocks.Add("Tool returned an unsupported image source.");
                            unsupported = true;
                        }

                        break;
                    default:
                        blocks.Add(
                            $"Tool returned unsupported {content.Modality.Value} content.");
                        unsupported = true;
                        break;
                }
            }

            converted.Add(new ToolResult(
                result.CallId,
                string.Join("\n\n", blocks),
                !unsupported && result.Status == MultimodalToolResultStatus.Success
                    ? ToolResultStatus.Success
                    : ToolResultStatus.Failure));
        }

        return converted;
    }

    private ImageAttachment CreateAttachment(ImageMedia image)
    {
        var number = _reserveImageNumber();
        return image.Source switch
        {
            InlineMediaSource inline => new ImageAttachment(
                number,
                image.MimeType.Value,
                inline.Data),
            UriMediaSource uri => new ImageAttachment(
                number,
                image.MimeType.Value,
                uri.Uri),
            _ => throw new NotSupportedException(
                $"Tool image source {image.Source.Kind.Value} is not supported yet.")
        };
    }

    private static void ReconcilePendingToolCalls(List<ChatItem> transcript)
    {
        var completed = new HashSet<string>(
            transcript.OfType<ToolResult>().Select(static result => result.CallId),
            StringComparer.Ordinal);
        foreach (var call in transcript.OfType<ToolCall>().ToArray())
        {
            if (completed.Add(call.CallId))
            {
                transcript.Add(new ToolResult(
                    call.CallId,
                    "Tool execution was interrupted by user.",
                    ToolResultStatus.Failure));
            }
        }
    }
}
