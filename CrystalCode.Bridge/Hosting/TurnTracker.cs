using CrystalCode.Engine.Events;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// Numbers engine turns in the order <see cref="TurnStarted"/> arrives, so a
/// prompt can wait for the turn it started or, for a follow-up sent while a
/// turn runs, the turn after it. The last error written during a turn is
/// kept as that turn's failure text. A clear drops queued follow-ups, so a
/// wait for a turn that had not started by then ends as abandoned.
/// </summary>
internal sealed class TurnTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<int, TaskCompletionSource<TurnOutcome>> _turns = [];
    private int _started;
    private int _clears;
    private bool _active;
    private bool _closed;
    private string? _lastError;

    public int Started
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    public int Clears
    {
        get
        {
            lock (_gate)
            {
                return _clears;
            }
        }
    }

    public void OnTurnStarted()
    {
        lock (_gate)
        {
            _started++;
            _active = true;
            _lastError = null;
        }
    }

    public void OnError(string text)
    {
        lock (_gate)
        {
            if (_active)
            {
                _lastError = text;
            }
        }
    }

    public void OnTurnFinished(TurnFinished finished) => Complete(finished);

    /// <summary>
    /// The running turn faulted. The engine wrote the error and will not
    /// publish <see cref="TurnFinished"/> for it.
    /// </summary>
    public void OnFaulted()
    {
        lock (_gate)
        {
            if (!_active)
            {
                return;
            }
        }

        Complete(null);
    }

    public void OnCleared()
    {
        TaskCompletionSource<TurnOutcome>[] dropped;
        lock (_gate)
        {
            _clears++;
            var keys = _turns.Keys.Where(key => key > _started).ToArray();
            dropped = [.. keys.Select(key => _turns[key])];
            foreach (var key in keys)
            {
                _turns.Remove(key);
            }
        }

        foreach (var completion in dropped)
        {
            completion.TrySetResult(new TurnOutcome(null, null, Abandoned: true));
        }
    }

    /// <summary>
    /// Waits for turn <paramref name="number"/>. <paramref name="clears"/> is
    /// <see cref="Clears"/> when the prompt was submitted.
    /// </summary>
    public Task<TurnOutcome> Turn(int number, int clears)
    {
        lock (_gate)
        {
            if (_closed || (clears != _clears && number > _started))
            {
                return Task.FromResult(new TurnOutcome(null, null, Abandoned: true));
            }

            return Slot(number).Task;
        }
    }

    /// <summary>Ends every wait: the session is gone.</summary>
    public void Abandon()
    {
        TaskCompletionSource<TurnOutcome>[] waiting;
        lock (_gate)
        {
            _closed = true;
            waiting = [.. _turns.Values];
            _turns.Clear();
            _active = false;
        }

        foreach (var completion in waiting)
        {
            completion.TrySetResult(new TurnOutcome(null, null, Abandoned: true));
        }
    }

    /// <summary>
    /// Completed slots stay, so a prompt that asks after its turn already
    /// ended still gets the outcome. One small entry per turn.
    /// </summary>
    private void Complete(TurnFinished? finished)
    {
        TaskCompletionSource<TurnOutcome> completion;
        string? error;
        lock (_gate)
        {
            _active = false;
            error = _lastError;
            _lastError = null;
            completion = Slot(_started);
        }

        completion.TrySetResult(new TurnOutcome(finished, error));
    }

    private TaskCompletionSource<TurnOutcome> Slot(int number)
    {
        if (!_turns.TryGetValue(number, out var completion))
        {
            completion = new TaskCompletionSource<TurnOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _turns[number] = completion;
        }

        return completion;
    }
}
