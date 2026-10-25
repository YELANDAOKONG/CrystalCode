using System.Threading.Channels;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Drives one <see cref="CodingSession"/> for a front end that has no frame
/// loop of its own. It is the only caller of the session's input methods:
/// inputs arrive on one channel and are handled in order, a finished turn is
/// collected with <see cref="CodingSession.CompleteTurnAsync"/> (which also
/// starts a queued follow-up), and input that arrives while a session call is
/// still running goes to <see cref="CodingSession.EnqueueAsync"/>, the same
/// way the terminal pump forwards keys. Interrupt, recall, mode, and the
/// other immediate operations stay direct calls on the session.
/// </summary>
public sealed class SessionRunner
{
    private readonly Channel<PendingInput> _inputs = Channel.CreateUnbounded<PendingInput>(
        new UnboundedChannelOptions { SingleReader = true });
    private Task<bool>? _wait;
    private int _running;

    public SessionRunner(CodingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Session = session;
    }

    public CodingSession Session { get; }

    /// <summary>
    /// Queues text exactly as the terminal composer submits it: a prompt, a
    /// slash command, a follow-up while a turn runs, or empty text that
    /// releases the queue.
    /// </summary>
    public Task<SessionInputResult> SubmitAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Post(new PendingInput(text, Quick: false));
    }

    /// <summary>
    /// Queues a quick prompt Send. It bypasses slash parsing and never takes
    /// the pending draft images.
    /// </summary>
    public Task<SessionInputResult> SubmitQuickPromptAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Post(new PendingInput(text, Quick: true));
    }

    /// <summary>
    /// Accepts no further input. <see cref="RunAsync"/> returns once the
    /// queued input is handled and no turn is running.
    /// </summary>
    public void Complete() => _inputs.Writer.TryComplete();

    /// <summary>
    /// Runs until the session asks to quit (returns true), until
    /// <see cref="Complete"/> was called and the session is idle, or until
    /// <paramref name="cancellationToken"/> is cancelled. Cancellation
    /// interrupts the running turn or compaction and collects it before
    /// returning false.
    /// </summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            throw new InvalidOperationException("The session runner is already running.");
        }

        using var interrupt = cancellationToken.Register(
            static state => ((CodingSession)state!).TryInterrupt(),
            Session);
        try
        {
            return await LoopAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DrainAsync();
            return false;
        }
        finally
        {
            _inputs.Writer.TryComplete();
            while (_inputs.Reader.TryRead(out var left))
            {
                left.Completion.TrySetCanceled(CancellationToken.None);
            }
        }
    }

    private Task<SessionInputResult> Post(PendingInput input)
    {
        if (!_inputs.Writer.TryWrite(input))
        {
            return Task.FromException<SessionInputResult>(
                new InvalidOperationException("The session no longer accepts input."));
        }

        return input.Completion.Task;
    }

    private async Task<bool> LoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var turn = Session.TurnTask;
            var read = WaitForInput(cancellationToken);
            if (turn is null)
            {
                await Task.WhenAny(read);
            }
            else
            {
                await Task.WhenAny(turn, read);
            }

            if (turn is { IsCompleted: true })
            {
                // CompleteTurnAsync publishes a faulted turn's error itself.
                if (await PumpAsync(Session.CompleteTurnAsync(CancellationToken.None), cancellationToken))
                {
                    return true;
                }

                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!read.Result)
            {
                if (turn is null)
                {
                    return false;
                }

                await turn.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                continue;
            }

            _wait = null;
            if (_inputs.Reader.TryRead(out var input) && await DispatchAsync(input, cancellationToken))
            {
                return true;
            }
        }
    }

    /// <summary>
    /// One outstanding wait on the reader at a time. A wait that lost a race
    /// against a turn or a pumped call is reused by the next loop.
    /// </summary>
    private Task<bool> WaitForInput(CancellationToken cancellationToken)
    {
        if (_wait is null || _wait.IsCanceled)
        {
            _wait = _inputs.Reader.WaitToReadAsync(cancellationToken).AsTask();
        }

        return _wait;
    }

    private async Task<bool> DispatchAsync(PendingInput input, CancellationToken cancellationToken)
    {
        try
        {
            var quit = false;
            if (input.Quick)
            {
                await PumpAsync(Session.SubmitQuickPromptAsync(input.Text, false, cancellationToken), cancellationToken);
            }
            else
            {
                quit = await PumpAsync(Session.SubmitAsync(input.Text, cancellationToken), cancellationToken);
            }

            input.Completion.TrySetResult(new SessionInputResult(Session.TurnActive, quit));
            return quit;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            input.Completion.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            // One bad input fails its own request; the session keeps running.
            input.Completion.TrySetException(exception);
            return false;
        }
    }

    private async Task<bool> PumpAsync(Task<bool> work, CancellationToken cancellationToken)
    {
        await PumpAsync((Task)work, cancellationToken);
        return await work;
    }

    /// <summary>
    /// Keeps accepting input while a session call runs. The call cannot be
    /// re-entered, so text goes to <see cref="CodingSession.EnqueueAsync"/> and
    /// a quick prompt is queued. Blank text is forwarded too, so a waiting
    /// follow-up can interrupt the turn or compaction.
    /// </summary>
    private async Task PumpAsync(Task work, CancellationToken cancellationToken)
    {
        while (!work.IsCompleted)
        {
            var read = WaitForInput(cancellationToken);
            await Task.WhenAny(work, read);
            if (work.IsCompleted || read.IsCanceled || !read.Result)
            {
                break;
            }

            _wait = null;
            while (!work.IsCompleted && _inputs.Reader.TryRead(out var input))
            {
                await EnqueueAsync(input, cancellationToken);
            }
        }

        await work;
    }

    private async Task EnqueueAsync(PendingInput input, CancellationToken cancellationToken)
    {
        try
        {
            if (input.Quick)
            {
                await Session.SubmitQuickPromptAsync(input.Text, true, cancellationToken);
            }
            else
            {
                await Session.EnqueueAsync(input.Text, cancellationToken);
            }

            input.Completion.TrySetResult(new SessionInputResult(Session.TurnActive, Quit: false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            input.Completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            input.Completion.TrySetException(exception);
        }
    }

    /// <summary>
    /// Collects every turn without starting queued follow-ups.
    /// </summary>
    private async Task DrainAsync()
    {
        while (Session.TurnTask is { } turn)
        {
            Session.TryInterrupt();
            await turn.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await Session.FinishTurnAsync(CancellationToken.None);
        }
    }

    private sealed record PendingInput(string Text, bool Quick)
    {
        public TaskCompletionSource<SessionInputResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
