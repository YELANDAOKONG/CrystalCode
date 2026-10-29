using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Sessions;
using CrystalCode.Server.Events;
using CrystalCode.Server.Interactions;
using CrystalCode.Server.Wire;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// The one live session a server process hosts: it prepares the launch
/// (asking clients for trust or a session choice), creates the
/// <see cref="CodingSession"/> with the server's front end, and runs it
/// through <see cref="SessionRunner"/> until quit or shutdown.
/// </summary>
public sealed class ServerSession
{
    private readonly ServerOptions _options;
    private readonly ServerRuntime _runtime;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SessionClosure?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _stateGate = new();
    private string _state = ServerState.Starting;
    private string? _failure;
    private string? _stopReason;

    public ServerSession(ServerOptions options, ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);
        _options = options;
        _runtime = runtime;
        Hub = new EventHub(options.EventLogCount, options.EventLogBytes);
        Broker = new InteractionBroker(Hub);
    }

    /// <summary>
    /// When this server process created its session host. A client that sees
    /// a different value has reached a new process.
    /// </summary>
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    public EventHub Hub { get; }

    public InteractionBroker Broker { get; }

    public string State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public string? Failure => _failure;

    /// <summary>Why the server is stopping, or null while it is not.</summary>
    public string? StopReason => Volatile.Read(ref _stopReason);

    /// <summary>Raised on every state change, with the new state.</summary>
    public event Action<string>? StateChanged;

    /// <summary>
    /// Records why the server is stopping and announces <c>closing</c> at
    /// once, before a running turn has finished unwinding. The first reason
    /// wins. The caller still cancels the run.
    /// </summary>
    public void RequestStop(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Interlocked.CompareExchange(ref _stopReason, reason, null) is null)
        {
            SetState(ServerState.Closing);
        }
    }

    /// <summary>The live session, or null before it is ready.</summary>
    public CodingSession? Session { get; private set; }

    public SessionRunner? Runner { get; private set; }

    public SessionLaunch? Launch { get; private set; }

    public QuickPromptStore? QuickPrompts { get; private set; }

    /// <summary>Completes when the session is ready, or faults when the launch failed.</summary>
    public Task Ready => _ready.Task;

    /// <summary>Completes after the session closed.</summary>
    public Task<SessionClosure?> Closed => _closed.Task;

    /// <summary>
    /// Prepares and runs the session. Returns when the session quit, when
    /// <paramref name="cancellationToken"/> requests shutdown, or when the
    /// launch failed.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Hub.BindSessionId(() => Session?.SessionId);
        try
        {
            var prompts = new StartupPrompts(Broker, SetState);
            var prepared = await SessionLauncher.PrepareAsync(_options.Launch, prompts, prompts, cancellationToken);
            if (prepared.Launch is not { } launch)
            {
                Fail(prepared.Failure == SessionLaunchFailureKind.Declined
                    ? "The operator declined to open the session."
                    : prepared.Error);
                return;
            }

            Launch = launch;
            foreach (var note in launch.Notes)
            {
                await _runtime.Error.WriteLineAsync(note);
            }

            var frontEnd = new SessionFrontEnd(Hub, Broker, Broker, Broker, Broker)
            {
                ChooseChildAsync = Broker.ChooseChildAsync
            };
            var session = launch.CreateSession(frontEnd, _runtime.Plugins);
            Broker.BindOrigin(() => session.CurrentOrigin);
            Session = session;
            var ran = false;
            try
            {
                await session.StartAsync(cancellationToken);
                if (launch.Mode is not null && !session.TrySetMode(launch.Mode, out var modeError))
                {
                    Fail(modeError);
                    return;
                }

                Runner = new SessionRunner(session);
                QuickPrompts = new QuickPromptStore(launch.Home);
                SetState(ServerState.Ready);
                _ready.TrySetResult();
                ran = true;
                if (await Runner.RunAsync(cancellationToken))
                {
                    RequestStop(ServerStopReason.Quit);
                }
            }
            finally
            {
                if (ran)
                {
                    RequestStop(ServerStopReason.Cancelled);
                }

                Broker.CancelAll();
                var closure = await session.CloseAsync(CancellationToken.None);
                Hub.Publish("server.closing", null, writer =>
                {
                    writer.WriteString("sessionId", closure.SessionId);
                    writer.WriteBoolean("saved", closure.Saved);
                    WireWriter.Optional(writer, "reason", StopReason);
                });
                _closed.TrySetResult(closure);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // An operator stop while the launch waits (for example on the trust
            // question) is an ordinary stop, not a failure.
            RequestStop(ServerStopReason.Cancelled);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Fail(exception.Message);
        }
        finally
        {
            Broker.CancelAll();
            _closed.TrySetResult(null);
        }
    }

    private void Fail(string error)
    {
        _failure = error;
        SetState(ServerState.Failed);
        _ready.TrySetException(new InvalidOperationException(error));
    }

    private void SetState(string state)
    {
        lock (_stateGate)
        {
            // Closing and failed are final; a late setter cannot reopen the server.
            if (_state == state || _state is ServerState.Closing or ServerState.Failed)
            {
                return;
            }

            _state = state;
        }

        Hub.Publish("server.state", null, writer =>
        {
            writer.WriteString("state", state);
            if (_failure is not null)
            {
                writer.WriteString("error", _failure);
            }

            if (state == ServerState.Closing)
            {
                WireWriter.Optional(writer, "reason", StopReason);
            }
        });
        StateChanged?.Invoke(state);
    }
}
