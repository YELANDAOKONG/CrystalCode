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
    private readonly ClientFeatures _features;
    private readonly object _gate = new();
    private readonly List<Action> _held = [];
    private readonly SessionState _state = new();
    private readonly Dictionary<int, int> _waiting = [];
    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);
    private readonly HashSet<int> _attached = [];
    private readonly CancellationTokenSource _stop = new();
    private string? _workspace;
    private SessionStore? _store;
    private QuickPromptStore? _quickPrompts;
    private ReportCapture? _capture;
    private bool _ready;
    private int _cancelThrough;
    private int _queued;
    private string? _questionCall;
    private bool _dropReplay;

    public BridgeSession(
        JsonRpcConnection connection,
        ClientFeatures features,
        IReadOnlySet<string> offeredOptions,
        string? acpId = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(offeredOptions);
        _connection = connection;
        _features = features;
        Id = acpId ?? string.Empty;
        Updates = new SessionUpdates(
            Send,
            () => Volatile.Read(ref _workspace),
            features.Notices,
            features.BooleanOptions,
            Image,
            features.Compaction,
            SendCrystal,
            offeredOptions);
        Children = new ChildUpdates(
            SendTo,
            child => new SessionUpdates(
                update => SendTo(child, update),
                () => Volatile.Read(ref _workspace),
                features.Notices,
                features.BooleanOptions,
                Image,
                features.Compaction,
                offered: offeredOptions),
            features.Subagents);
    }

    /// <summary>
    /// The ACP session id: the engine id the session opened with. It stays
    /// fixed through <c>/clear</c>, which gives the engine a new id.
    /// </summary>
    public string Id { get; private set; } = string.Empty;

    public CodingSession? Session { get; private set; }

    public SessionRunner? Runner { get; private set; }

    public TurnTracker Turns { get; } = new();

    public SessionUpdates Updates { get; }

    public ChildUpdates Children { get; }

    /// <summary>The reducer behind <c>_crystal/snapshot</c>, fed every event.</summary>
    public SessionSnapshot Snapshot()
    {
        lock (_state)
        {
            return _state.Snapshot();
        }
    }

    public Task Running { get; private set; } = Task.CompletedTask;

    /// <summary>The live workspace root, from the launch and then from chrome.</summary>
    public string? Workspace => Volatile.Read(ref _workspace);

    /// <summary>Follow-ups waiting for the next turn, from the last queue report.</summary>
    public int Queued => Volatile.Read(ref _queued);

    public void Bind(CodingSession session, string workspace, CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentNullException.ThrowIfNull(home);
        Session = session;
        _store = new SessionStore(home);
        _quickPrompts = new QuickPromptStore(home);
        if (Id.Length == 0)
        {
            Id = session.SessionId;
        }

        Updates.EngineSessionId = session.SessionId;
        Volatile.Write(ref _workspace, workspace);
    }

    /// <summary>
    /// After a turn: <c>session_info_update</c> with the saved title (the
    /// first user message, as <c>session/list</c> shows it) and the time.
    /// </summary>
    public void Saved()
    {
        if (Session is { } session && _store is { } store && store.TrySummarize(session.SessionId, out var summary))
        {
            Updates.Saved(summary.Preview);
        }
    }

    /// <summary>The F6 catalog in Home, for <c>_crystal/quickPrompts/*</c>.</summary>
    public QuickPromptStore QuickPrompts => _quickPrompts!;

    /// <summary>Records an image from <c>_crystal/image/attach</c> that a prompt may name as plain text.</summary>
    public void Attached(int number)
    {
        lock (_gate)
        {
            _attached.Add(number);
        }
    }

    /// <summary>Takes an attached number once; see <see cref="PromptContent.TrustAttached"/>.</summary>
    public bool TakeAttached(int number)
    {
        lock (_gate)
        {
            return _attached.Remove(number);
        }
    }

    /// <summary>Captures the structured values of the next report of <paramref name="kind"/>.</summary>
    public ReportCapture CaptureReport(string kind)
    {
        var capture = new ReportCapture(kind, done => Interlocked.CompareExchange(ref _capture, null, done));
        Volatile.Write(ref _capture, capture);
        return capture;
    }

    /// <summary>True for the ACP id or the current engine id.</summary>
    public bool Answers(string id) =>
        string.Equals(id, Id, StringComparison.Ordinal)
        || (Session is { } session && string.Equals(id, session.SessionId, StringComparison.Ordinal));

    /// <summary>Starts the input loop. Updates stay held until <see cref="Release"/>.</summary>
    public void Run(Action stopped)
    {
        ArgumentNullException.ThrowIfNull(stopped);
        Runner = new SessionRunner(Session!);
        Running = RunCoreAsync(stopped);
    }

    private async Task RunCoreAsync(Action stopped)
    {
        try
        {
            await Runner!.RunAsync(_stop.Token);
        }
        finally
        {
            stopped();
        }
    }

    /// <summary>The client has the session id; send what was held.</summary>
    public void Release()
    {
        Action[] held;
        lock (_gate)
        {
            _ready = true;
            held = [.. _held];
            _held.Clear();
        }

        foreach (var notify in held)
        {
            notify();
        }
    }

    /// <summary>
    /// A prompt waits for turn <paramref name="number"/> until the returned
    /// scope is disposed, so a cancel that arrives first still reaches it.
    /// </summary>
    public IDisposable Await(int number)
    {
        lock (_gate)
        {
            _waiting[number] = _waiting.GetValueOrDefault(number) + 1;
        }

        return new Waiting(this, number);
    }

    /// <summary>
    /// <c>session/resume</c> restores without replay, but the engine replays
    /// a resumed archive when it starts. Drop that one replay.
    /// </summary>
    public void DropStartReplay() => Volatile.Write(ref _dropReplay, true);

    /// <summary>The start replay is over; a later <c>/resume</c> replays as usual.</summary>
    public void EndStartReplay() => Volatile.Write(ref _dropReplay, false);

    /// <summary>
    /// <c>session/cancel</c>: interrupt the running turn, and every turn a
    /// waiting prompt was promised, as soon as it starts. A queued follow-up
    /// would otherwise start once the interrupted turn is collected.
    /// </summary>
    public void Cancel()
    {
        lock (_gate)
        {
            var promised = _waiting.Count == 0 ? 0 : _waiting.Keys.Max();
            _cancelThrough = Math.Max(_cancelThrough, Math.Max(promised, Turns.Started));
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

    /// <summary>
    /// The last error the engine wrote for input submitted under
    /// <paramref name="origin"/>, such as a refused command. Turn errors carry
    /// no origin and are not reported here.
    /// </summary>
    public string? TakeError(string origin)
    {
        lock (_gate)
        {
            return _errors.Remove(origin, out var text) ? text : null;
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
                if (error.Origin is { } origin)
                {
                    lock (_gate)
                    {
                        _errors[origin] = error.Text;
                    }
                }

                if (Session?.TurnTask is { IsFaulted: true })
                {
                    // FinishTurnAsync writes the fault before it clears the
                    // task and returns without TurnFinished.
                    Turns.OnFaulted();
                }

                break;
            case QueueChanged queue:
                Volatile.Write(ref _queued, queue.Items.Count);
                break;
            case ConversationCleared or HistoryReplayed:
                // /clear and /resume discard queued follow-ups.
                Turns.OnCleared();
                lock (_gate)
                {
                    // The engine drops its images with the conversation.
                    _attached.Clear();
                }

                if (Session is { } current)
                {
                    Updates.EngineSessionId = current.SessionId;
                }

                break;
            case ToolCallsIssued calls:
                // The question tool asks through IUserPrompt without its call id.
                Volatile.Write(
                    ref _questionCall,
                    calls.Calls.LastOrDefault(static call => call.Name == QuestionTool.ToolName)?.CallId);
                break;
            default:
                break;
        }

        Volatile.Read(ref _capture)?.Accept(sessionEvent);
        if (sessionEvent is HistoryReplayed && Volatile.Read(ref _dropReplay))
        {
            return;
        }

        lock (_state)
        {
            _state.Apply(sessionEvent);
        }

        if (sessionEvent is ChildSessionChanged or ChildEventReceived)
        {
            Children.Apply(sessionEvent, Id);
            return;
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
        var reusable = classification.AllowsReusableGrant;
        var workspace = Workspace;
        Action<Utf8JsonWriter> request;
        if (Children.TryFind(call, out var child, out var childUpdates))
        {
            if (childUpdates is not null)
            {
                // Subagent sessions: the child's own call, under the child session id.
                childUpdates.EnsureAnnounced(call);
                request = writer => PermissionWire.Request(writer, child.Id, call, classification, review, extendedReview, workspace);
            }
            else
            {
                // Plain ACP: the parent session and its task call.
                var parent = Id;
                request = writer => PermissionWire.ChildRequest(writer, parent, child, call, classification, review, extendedReview);
            }
        }
        else
        {
            Updates.EnsureAnnounced(call);
            var parent = Id;
            request = writer => PermissionWire.Request(writer, parent, call, classification, review, extendedReview, workspace);
        }

        JsonElement response;
        try
        {
            response = await _connection.RequestAsync("session/request_permission", request, cancellationToken);
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

    /// <summary>
    /// The question tool and MCP elicitation as one <c>elicitation/create</c>
    /// form. Without form support, or on anything but a valid accept, the
    /// questions are dismissed and the model gets the dismissed result.
    /// </summary>
    public async ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var dismissed = new QuestionResponse([], true);
        if (!_features.Forms || questions.Count == 0)
        {
            return dismissed;
        }

        var call = Volatile.Read(ref _questionCall);
        var result = await AskFormAsync(writer => FormWire.Questions(writer, Id, call, questions), cancellationToken);
        return result is { } value ? FormWire.Answers(value, questions) : dismissed;
    }

    /// <summary><c>/resume</c> without an id, as a form; declined without forms.</summary>
    public async Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (!_features.Forms || sessions.Count == 0)
        {
            return null;
        }

        var result = await AskFormAsync(writer => FormWire.Sessions(writer, Id, sessions, listWorkspace), cancellationToken);
        return result is { } value ? FormWire.Chosen(value, sessions.Select(static session => session.Id)) : null;
    }

    /// <summary><c>/child</c> without an id, as a form; declined without forms.</summary>
    public async Task<string?> ChooseChildAsync(IReadOnlyList<ChildSessionDocument> children, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (!_features.Forms || children.Count == 0)
        {
            return null;
        }

        var result = await AskFormAsync(writer => FormWire.Children(writer, Id, children), cancellationToken);
        return result is { } value ? FormWire.Chosen(value, children.Select(static child => child.Id)) : null;
    }

    /// <summary>
    /// Trust inside a running session (<c>/cd</c>, <c>/resume</c> of another
    /// workspace), as a form. Refused without forms.
    /// </summary>
    public async ValueTask<bool> ConfirmAsync(WorkspaceTrustRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_features.Forms)
        {
            return false;
        }

        var result = await AskFormAsync(writer => FormWire.Trust(writer, Id, null, request), cancellationToken);
        return result is { } value && FormWire.Trusted(value);
    }

    /// <summary>The form result, or null when the client failed, closed, or the turn ended.</summary>
    private async Task<JsonElement?> AskFormAsync(Action<Utf8JsonWriter> form, CancellationToken cancellationToken)
    {
        return await AskFormAsync(_connection, form, cancellationToken);
    }

    /// <inheritdoc cref="AskFormAsync(Action{Utf8JsonWriter}, CancellationToken)"/>
    internal static async Task<JsonElement?> AskFormAsync(
        JsonRpcConnection connection,
        Action<Utf8JsonWriter> form,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connection.RequestAsync("elicitation/create", form, cancellationToken);
        }
        catch (JsonRpcException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private (string MimeType, byte[] Data)? Image(int number) =>
        Session is { } session
        && session.TryGetImage(number, out var image)
        && image is { Data: { } data }
            ? (image.MimeType, data.ToArray())
            : null;

    private void Send(Action<Utf8JsonWriter> update)
        => SendTo(null, update);

    /// <summary>
    /// Sends one update under <paramref name="sessionId"/>, or the ACP id when
    /// null. Updates are held, in order, until the client has the session id.
    /// </summary>
    private void SendTo(string? sessionId, Action<Utf8JsonWriter> update) =>
        Enqueue(() => _connection.Notify("session/update", writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", sessionId ?? Id);
            writer.WritePropertyName("update");
            update(writer);
            writer.WriteEndObject();
        }));

    /// <summary>A <c>_crystal/*</c> notification in the <c>session/update</c> envelope.</summary>
    private void SendCrystal(string method, Action<Utf8JsonWriter> fields)
    {
        if (!_features.Extensions)
        {
            // A client that never opted in sees plain ACP.
            return;
        }

        Enqueue(() => _connection.Notify(method, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("sessionId", Id);
            fields(writer);
            writer.WriteEndObject();
        }));
    }

    private void Enqueue(Action notify)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                _held.Add(notify);
                return;
            }
        }

        notify();
    }

    private void Release(int number)
    {
        lock (_gate)
        {
            var count = _waiting.GetValueOrDefault(number) - 1;
            if (count <= 0)
            {
                _waiting.Remove(number);
            }
            else
            {
                _waiting[number] = count;
            }
        }
    }

    private sealed class Waiting(BridgeSession session, int number) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                session.Release(number);
            }
        }
    }
}
