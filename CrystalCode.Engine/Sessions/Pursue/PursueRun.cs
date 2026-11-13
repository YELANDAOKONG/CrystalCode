using System.Diagnostics;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>
/// One pursue run: the working model runs a segment, the checker judges it,
/// and the run continues, waits, or stops. It is one logical turn, so the
/// caller sees one <see cref="TurnResult"/> with the counts of every segment.
/// Checker calls are not in that result; the host records them on the ledger.
/// </summary>
internal sealed class PursueRun
{
    private readonly IPursueHost _host;
    private readonly PursueLimits _limits;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private PursueState _state;
    private IReadOnlyList<ChatItem> _transcript = [];
    private int _modelCalls;
    private int _toolCalls;
    private TokenUsage? _lastUsage;
    private TokenUsage? _accumulated;
    private bool _accumulatedComplete = true;

    public PursueRun(IPursueHost host, PursueLimits limits, PursueState state)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(state);
        _host = host;
        _limits = limits;
        _state = state with { Checks = 0, Continuations = 0, Stalls = 0, RunId = Guid.NewGuid().ToString("N") };
    }

    public async Task<TurnResult> RunAsync(
        IReadOnlyList<ChatItem> input,
        bool resumed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        _transcript = [.. input];
        if (resumed)
        {
            AppendContinuation(resumed: true);
        }

        var lastNextStep = string.Empty;
        var needSegment = true;
        var segmentToolCalls = 0;
        while (true)
        {
            if (needSegment)
            {
                SetPhase(PursuePhase.Working);
                var remaining = Remaining();
                if (remaining is { } left && left <= TimeSpan.Zero)
                {
                    return Stop(TurnStopReason.DurationLimitReached, PursueReasonCodes.DurationLimit, "The pursue duration limit was reached.");
                }

                var segment = await _host.RunSegmentAsync(
                    _transcript,
                    new TurnLimits(null, null, remaining),
                    cancellationToken);
                Add(segment);
                segmentToolCalls = segment.ToolCallCount;
                if (segment.StopReason != TurnStopReason.Completed)
                {
                    return StopForSegment(segment.StopReason);
                }

                if (_host.HasQueuedInput)
                {
                    return Stop(TurnStopReason.Completed, PursueReasonCodes.InputPending, "New input arrived, so the pursuit paused.");
                }
            }

            if (!_host.TryReserveCheck())
            {
                return Stop(TurnStopReason.ModelCallLimitReached, PursueReasonCodes.ModelLimit, "The pursue model call limit was reached.");
            }

            SetPhase(PursuePhase.Checking);
            var check = await CheckAsync(cancellationToken);
            if (check is null)
            {
                Record(PursueCheck.Failed(PursueCheckFailure.Request,
                    cancellationToken.IsCancellationRequested ? "The check was interrupted." : "The check timed out."));
                return TimedOutOrInterrupted(cancellationToken);
            }

            Record(check);
            if (check.Verdict is not { } verdict)
            {
                return StopForCheckFailure(check);
            }

            switch (verdict.Kind)
            {
                case PursueVerdictKind.Done:
                    _state = _state.Stopped(PursueStatus.Complete, PursueReasonCodes.Done, verdict.Reason);
                    return Finish(TurnStopReason.Completed);
                case PursueVerdictKind.Blocked:
                    return Stop(TurnStopReason.PursueBlocked, PursueReasonCodes.Blocked, verdict.Reason, PursueStatus.Blocked);
                case PursueVerdictKind.Wait:
                    var waited = await WaitAsync(verdict, cancellationToken);
                    if (waited is not null)
                    {
                        return waited;
                    }

                    needSegment = false;
                    continue;
                case PursueVerdictKind.Continue:
                    break;
                default:
                    throw new InvalidOperationException("The verdict kind is not supported.");
            }

            if (_host.HasQueuedInput)
            {
                return Stop(TurnStopReason.Completed, PursueReasonCodes.InputPending, "New input arrived, so the pursuit paused.");
            }

            var noProgress = needSegment && segmentToolCalls == 0;
            var repeated = string.Equals(Normalize(verdict.NextStep), lastNextStep, StringComparison.Ordinal);
            lastNextStep = Normalize(verdict.NextStep);
            _state = _state with
            {
                Stalls = noProgress || repeated ? _state.Stalls + 1 : 0,
                NextStep = verdict.NextStep
            };
            if (_state.Stalls >= _limits.MaximumStalls)
            {
                return Stop(TurnStopReason.PursueLimitReached, PursueReasonCodes.Stalled, "The run made no progress for several checks in a row.");
            }

            if (_limits.MaximumContinuations is int maximum && _state.Continuations >= maximum)
            {
                return Stop(TurnStopReason.PursueLimitReached, PursueReasonCodes.ContinuationLimit, "The pursue continuation limit was reached.");
            }

            _state = _state with { Continuations = _state.Continuations + 1 };
            AppendContinuation(resumed: false, verdict);
            needSegment = true;
        }
    }

    private void Record(PursueCheck check)
    {
        var verdict = check.Verdict;
        var record = new PursueCheckRecord(
            Guid.NewGuid().ToString("N"), _state.Id, _state.RunId, _state.Checks + 1,
            DateTimeOffset.UtcNow,
            verdict is null ? "failed" : PursueVerdict.Token(verdict.Kind),
            verdict?.Reason ?? check.Error, verdict?.NextStep ?? string.Empty,
            check.Evidence, verdict?.WaitFor ?? [], check.Usage);
        _state = _state with { Checks = record.Index, History = [.. _state.History, record] };
        _host.Publish(_state);
        _host.Publish(new PursueChecked(record.Index, record.Verdict, record.Reason, record.NextStep,
            verdict?.Evidence ?? [], record.Usage, record));
    }

    private async Task<PursueCheck?> CheckAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (Remaining() is { } remaining)
        {
            linked.CancelAfter(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining);
        }

        try
        {
            return await _host.CheckAsync(_transcript, _state, linked.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<TurnResult?> WaitAsync(PursueVerdict verdict, CancellationToken cancellationToken)
    {
        if (verdict.WaitFor.Any(id => !_host.TaskExists(id)))
        {
            _host.Error("The checker named a background task that does not exist in this session.");
            return Stop(TurnStopReason.Failed, PursueReasonCodes.ReviewInvalid, "The checker named an unknown background task.");
        }

        SetPhase(PursuePhase.Waiting);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = _limits.MaximumWaitSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        var remaining = Remaining();
        if (remaining is { } left && (timeout is null || left < timeout))
        {
            timeout = left;
        }

        if (timeout is { } limit)
        {
            linked.CancelAfter(limit < TimeSpan.Zero ? TimeSpan.Zero : limit);
        }

        try
        {
            await _host.WaitAnyAsync(verdict.WaitFor, linked.Token);
            return _host.HasQueuedInput
                ? Stop(TurnStopReason.Completed, PursueReasonCodes.InputPending, "New input arrived, so the pursuit paused.")
                : null;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Stop(TurnStopReason.Interrupted, PursueReasonCodes.Interrupted, "The pursuit was interrupted.");
            }

            return Remaining() is { } after && after <= TimeSpan.Zero
                ? Stop(TurnStopReason.DurationLimitReached, PursueReasonCodes.DurationLimit, "The pursue duration limit was reached.")
                : Stop(TurnStopReason.PursueLimitReached, PursueReasonCodes.WaitTimeout, "Waiting for a background task timed out.");
        }
    }

    private TurnResult TimedOutOrInterrupted(CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Stop(TurnStopReason.Interrupted, PursueReasonCodes.Interrupted, "The pursuit was interrupted.")
            : Stop(TurnStopReason.DurationLimitReached, PursueReasonCodes.DurationLimit, "The pursue duration limit was reached.");

    private TurnResult StopForCheckFailure(PursueCheck check)
    {
        switch (check.Failure)
        {
            case PursueCheckFailure.Overflow:
                _host.Error(check.Error);
                return Stop(TurnStopReason.ContextOverflow, PursueReasonCodes.ContextOverflow, check.Error);
            case PursueCheckFailure.Invalid:
                _host.Error(check.Error);
                return Stop(TurnStopReason.Failed, PursueReasonCodes.ReviewInvalid, check.Error);
            case PursueCheckFailure.Request:
            case PursueCheckFailure.None:
            default:
                _host.Error(check.Error);
                return Stop(TurnStopReason.Failed, PursueReasonCodes.ReviewFailed, check.Error);
        }
    }

    private TurnResult StopForSegment(TurnStopReason stop)
    {
        if (stop == TurnStopReason.Interrupted)
        {
            return Stop(stop, PursueReasonCodes.Interrupted, "The pursuit was interrupted.");
        }

        if (stop == TurnStopReason.ModelCallLimitReached)
        {
            return Stop(stop, PursueReasonCodes.ModelLimit, "The pursue model call limit was reached.");
        }

        if (stop == TurnStopReason.ToolCallLimitReached)
        {
            return Stop(stop, PursueReasonCodes.ToolLimit, "The pursue tool call limit was reached.");
        }

        if (stop == TurnStopReason.DurationLimitReached)
        {
            return Stop(stop, PursueReasonCodes.DurationLimit, "The pursue duration limit was reached.");
        }

        if (stop == TurnStopReason.ContextOverflow)
        {
            return Stop(stop, PursueReasonCodes.ContextOverflow, "The working model ran out of context.");
        }

        return Stop(stop, PursueReasonCodes.RunFailed, "The working model stopped with " + stop.Value + ".");
    }

    private void AppendContinuation(bool resumed, PursueVerdict? verdict = null)
    {
        var remaining = _limits.MaximumContinuations is int maximum
            ? Math.Max(0, maximum - _state.Continuations)
            : int.MaxValue;
        var state = verdict is null
            ? _state
            : _state with { Reason = verdict.Reason, NextStep = verdict.NextStep };
        var message = _host.BuildContinuation(state, remaining, resumed);
        _host.CommitContinuation(message);
        _transcript = [.. _transcript, message];
    }

    private void Add(TurnResult segment)
    {
        _transcript = segment.Transcript;
        _modelCalls = checked(_modelCalls + segment.ModelCallCount);
        _toolCalls = checked(_toolCalls + segment.ToolCallCount);
        if (segment.Usage is not null)
        {
            _lastUsage = segment.Usage;
        }

        if (segment.ModelCallCount == 0 || !_accumulatedComplete)
        {
            return;
        }

        var added = segment.AccumulatedUsage ?? (segment.ModelCallCount == 1 ? segment.Usage : null);
        if (added is null)
        {
            _accumulated = null;
            _accumulatedComplete = false;
            return;
        }

        _accumulated = SessionLedger.Add(_accumulated, added);
    }

    private TimeSpan? Remaining() =>
        _limits.MaximumDurationSeconds is int seconds
            ? TimeSpan.FromSeconds(seconds) - _clock.Elapsed
            : null;

    private void SetPhase(PursuePhase phase)
    {
        _state = _state.Active(phase);
        _host.Publish(_state);
    }

    private TurnResult Stop(
        TurnStopReason stop,
        string reasonCode,
        string reason,
        PursueStatus status = PursueStatus.Paused)
    {
        _state = _state.Stopped(status, reasonCode, reason);
        return Finish(stop);
    }

    private TurnResult Finish(TurnStopReason stop)
    {
        _host.Publish(_state);
        return new TurnResult(
            stop,
            _modelCalls,
            _toolCalls,
            _lastUsage,
            _transcript,
            _accumulatedComplete ? _accumulated : null);
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
