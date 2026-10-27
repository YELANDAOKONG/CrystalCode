using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Bridge.Transport;
using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Sessions.Interfaces;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// One live <see cref="CodingSession"/> behind one ACP session id: the
/// engine front end (observer and prompts), the runner, and the turn
/// tracker prompts wait on. Updates published before the client has the
/// session id are held and sent after the <c>session/new</c> response.
/// </summary>
internal sealed class BridgeSession : ISessionObserver, IApprovalPrompt, IUserPrompt, ISessionChooser, IWorkspaceTrustPrompt
{
    private readonly JsonRpcConnection _connection;
    private readonly object _gate = new();
    private readonly List<Action<Utf8JsonWriter>> _held = [];
    private readonly CancellationTokenSource _stop = new();
    private string? _workspace;
    private bool _ready;
    private int _awaited;
    private int _cancelThrough;

    public BridgeSession(JsonRpcConnection connection, ClientFeatures features)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(features);
        _connection = connection;
        Updates = new SessionUpdates(Send, () => Volatile.Read(ref _workspace), features.Notices);
    }

    /// <summary>The ACP session id. It stays fixed for the connection.</summary>
    public string Id { get; private set; } = string.Empty;

    public CodingSession? Session { get; private set; }

    public SessionRunner? Runner { get; private set; }

    public TurnTracker Turns { get; } = new();

    public SessionUpdates Updates { get; }

    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>The live workspace root, from the launch and then from chrome.</summary>
    public string? Workspace => Volatile.Read(ref _workspace);

    public void Bind(CodingSession session, string workspace)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        Session = session;
        Id = session.SessionId;
        Volatile.Write(ref _workspace, workspace);
    }

    /// <summary>Starts the input loop. Updates stay held until <see cref="Release"/>.</summary>
    public void Run(Action<bool> stopped)
    {
        ArgumentNullException.ThrowIfNull(stopped);
        Runner = new SessionRunner(Session!);
        Running = RunCoreAsync(stopped);
    }

    private async Task RunCoreAsync(Action<bool> stopped)
    {
        var quit = false;
        try
        {
            quit = await Runner!.RunAsync(_stop.Token);
        }
        finally
        {
            stopped(quit);
        }
    }

    /// <summary>The client has the session id; send what was held.</summary>
    public void Release()
    {
        Action<Utf8JsonWriter>[] held;
        lock (_gate)
        {
            _ready = true;
            held = [.. _held];
            _held.Clear();
        }

        foreach (var update in held)
        {
            Notify(update);
        }
    }

    /// <summary>A prompt waits for turn <paramref name="number"/>.</summary>
    public void Await(int number)
    {
        lock (_gate)
        {
            _awaited = Math.Max(_awaited, number);
        }
    }

    /// <summary>
    /// <c>session/cancel</c>: interrupt the running turn, and every turn a
    /// waiting prompt was promised, as soon as it starts. A queued follow-up
    /// would otherwise start once the interrupted turn is collected.
    /// </summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _cancelThrough = Math.Max(_cancelThrough, Math.Max(_awaited, Turns.Started));
        }

        Session?.TryInterrupt();
    }

    public bool CancelRequested(int turn)
    {
        lock (_gate)
        {
            return turn <= _cancelThrough;
        }
    }

    /// <summary>Stops the runner, collects the turn, and closes the session.</summary>
    public async Task<SessionClosure?> CloseAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync();
        }

        await Running.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Turns.Abandon();
        var closure = Session is { } session ? await session.CloseAsync(CancellationToken.None) : null;
        return closure;
    }

    public void OnEvent(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        switch (sessionEvent)
        {
            case SessionStarted started:
                Volatile.Write(ref _workspace, started.Chrome.WorkspaceRoot);
                break;
            case ChromeChanged changed:
                Volatile.Write(ref _workspace, changed.Chrome.WorkspaceRoot);
                break;
            case TurnStarted:
                Turns.OnTurnStarted();
                if (CancelRequested(Turns.Started))
                {
                    // The turn source exists before TurnStarted is published,
                    // so the turn stops before its first model round.
                    Session?.TryInterrupt();
                }

                break;
            case TurnFinished finished:
                Turns.OnTurnFinished(finished);
                break;
            case ErrorWritten error:
                Turns.OnError(error.Text);
                break;
            case QueueChanged when Session is { TurnActive: false }:
                // A turn that faulted publishes no TurnFinished; collecting it
                // republishes the queue once the session is idle again.
                Turns.OnIdle();
                break;
            case ConversationCleared:
                Turns.OnCleared();
                break;
            default:
                break;
        }

        Updates.Apply(sessionEvent);
    }

    public async ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        Updates.EnsureAnnounced(call);
        var reusable = classification.AllowsReusableGrant;
        var workspace = Workspace;
        JsonElement response;
        try
        {
            response = await _connection.RequestAsync(
                "session/request_permission",
                writer => PermissionWire.Request(writer, Id, call, classification, review, extendedReview, workspace),
                cancellationToken);
        }
        catch (JsonRpcException)
        {
            return ApprovalChoice.Deny;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The connection closed while the client was asked.
            return ApprovalChoice.Deny;
        }

        return PermissionWire.Choice(response, reusable);
    }

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null,
        ApprovalReviewVerdict? extendedReview = null)
    {
        // The engine publishes ApprovalPassed, which the updates map.
        ArgumentNullException.ThrowIfNull(call);
    }

    public void NotifyReviewing(ToolCall call, ApprovalReviewStage stage)
    {
        // The engine publishes ApprovalReviewing, which the updates map.
        ArgumentNullException.ThrowIfNull(call);
    }

    /// <summary>Forms arrive in a later phase; until then the question is dismissed.</summary>
    public ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);
        return ValueTask.FromResult(new QuestionResponse([], true));
    }

    public Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return Task.FromResult<string?>(null);
    }

    public ValueTask<bool> ConfirmAsync(WorkspaceTrustRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ValueTask.FromResult(false);
    }

    private void Send(Action<Utf8JsonWriter> update)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                _held.Add(update);
                return;
            }
        }

        Notify(update);
    }

    private void Notify(Action<Utf8JsonWriter> update) =>
        _connection.Notify("session/update", writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", Id);
            writer.WritePropertyName("update");
            update(writer);
            writer.WriteEndObject();
        });
}
