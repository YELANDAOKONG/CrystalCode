using System.Threading.Channels;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Drives one <see cref="CodingSession"/> for a front end that has no frame
/// loop of its own. It is the only caller of the session's input methods:
/// work arrives on one channel and runs in order, a finished turn is
/// collected with <see cref="CodingSession.CompleteTurnAsync"/> (which also
/// starts a queued follow-up), and text that arrives while a session call is
/// still running goes to <see cref="CodingSession.EnqueueAsync"/>, the same
/// way the terminal pump forwards keys. Operations such as a mode change or
/// a draft update also run here, in order with the input, even while a call
/// is running. Interrupt, recall, and side-question cancellation stay direct
/// calls on the session because Ctrl+C makes them from any thread.
/// </summary>
public sealed class SessionRunner
{
    private readonly Channel<Work> _inputs = Channel.CreateUnbounded<Work>(
        new UnboundedChannelOptions { SingleReader = true });
    private Task<bool>? _wait;
    private bool _pumped;
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
    /// releases the queue. Events it produces carry <paramref name="origin"/>.
    /// </summary>
    public Task<SessionInputResult> SubmitAsync(string text, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var work = new InputWork(text, Quick: false, origin);
        return Post(work, work.Completion.Task);
    }

    /// <summary>
    /// Queues a quick prompt Send. It bypasses slash parsing and never takes
    /// the pending draft images.
    /// </summary>
    public Task<SessionInputResult> SubmitQuickPromptAsync(string text, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var work = new InputWork(text, Quick: true, origin);
        return Post(work, work.Completion.Task);
    }

    /// <summary>
    /// Runs a synchronous session operation in order with the queued input,
    /// under <paramref name="origin"/>.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<CodingSession, T> operation, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var work = new OperationWork<T>(operation, origin);
        return Post(work, work.Completion.Task);
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
                left.Cancel(CancellationToken.None);
            }
        }
    }

    private Task<T> Post<T>(Work work, Task<T> completion)
    {
        if (!_inputs.Writer.TryWrite(work))
        {
            return Task.FromException<T>(
                new InvalidOperationException("The session no longer accepts input."));
        }

        return completion;
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
            if (!_inputs.Reader.TryRead(out var work))
            {
                continue;
            }

            _pumped = false;
            if (await RunWorkAsync(work, idle: true, cancellationToken))
            {
                return true;
            }

            // Text that arrived while a command ran was queued. With no turn
            // to collect, nothing else would start it.
            if (_pumped
                && Session.TurnTask is null
                && await PumpAsync(Session.CompleteTurnAsync(CancellationToken.None), cancellationToken))
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

    private async Task<bool> RunWorkAsync(Work work, bool idle, CancellationToken cancellationToken)
    {
        try
        {
            using (Session.BeginOrigin(work.Origin))
            {
                return await work.RunAsync(this, idle, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            work.Cancel(cancellationToken);
            if (idle)
            {
                throw;
            }

            return false;
        }
        catch (Exception exception)
        {
            // One bad input fails its own request; the session keeps running.
            work.Fail(exception);
            return false;
        }
    }

    private async Task<bool> PumpAsync(Task<bool> work, CancellationToken cancellationToken)
    {
        await PumpAsync((Task)work, cancellationToken);
        return await work;
    }

    /// <summary>
    /// Keeps accepting work while a session call runs. The call cannot be
    /// re-entered, so text goes to <see cref="CodingSession.EnqueueAsync"/>, a
    /// quick prompt is queued, and operations run between the call's awaits.
    /// Blank text is forwarded too, so a waiting follow-up can interrupt the
    /// turn or compaction.
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
            while (!work.IsCompleted && _inputs.Reader.TryRead(out var pending))
            {
                _pumped = true;
                await RunWorkAsync(pending, idle: false, cancellationToken);
            }
        }

        await work;
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

    private abstract class Work(string? origin)
    {
        public string? Origin { get; } = origin;

        /// <summary>
        /// Runs the work. <paramref name="idle"/> is false while another session
        /// call is still running. Returns true when the session asked to quit.
        /// </summary>
        public abstract Task<bool> RunAsync(SessionRunner runner, bool idle, CancellationToken cancellationToken);

        public abstract void Cancel(CancellationToken cancellationToken);

        public abstract void Fail(Exception exception);
    }

    private sealed class InputWork(string text, bool Quick, string? origin) : Work(origin)
    {
        public TaskCompletionSource<SessionInputResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<bool> RunAsync(SessionRunner runner, bool idle, CancellationToken cancellationToken)
        {
            var session = runner.Session;
            var quit = false;
            var capture = new SessionInputCapture();
            using var scope = session.CaptureInput(capture);
            if (!idle)
            {
                if (Quick)
                {
                    await session.SubmitQuickPromptAsync(text, true, cancellationToken);
                }
                else
                {
                    await session.EnqueueAsync(text, cancellationToken);
                }
            }
            else if (Quick)
            {
                await runner.PumpAsync(session.SubmitQuickPromptAsync(text, false, cancellationToken), cancellationToken);
            }
            else
            {
                quit = await runner.PumpAsync(session.SubmitAsync(text, cancellationToken), cancellationToken);
            }

            Completion.TrySetResult(new SessionInputResult(session.TurnActive, quit, capture.Error, capture.Pending));
            return quit;
        }

        public override void Cancel(CancellationToken cancellationToken) =>
            Completion.TrySetCanceled(cancellationToken);

        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }

    private sealed class OperationWork<T>(Func<CodingSession, T> operation, string? origin) : Work(origin)
    {
        public TaskCompletionSource<T> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<bool> RunAsync(SessionRunner runner, bool idle, CancellationToken cancellationToken)
        {
            Completion.TrySetResult(operation(runner.Session));
            return Task.FromResult(false);
        }

        public override void Cancel(CancellationToken cancellationToken) =>
            Completion.TrySetCanceled(cancellationToken);

        public override void Fail(Exception exception) => Completion.TrySetException(exception);
    }
}
