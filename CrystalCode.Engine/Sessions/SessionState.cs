using System.Text;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Front-end-neutral reducer over <see cref="SessionEvent"/>. A front end that
/// must redraw a session at any time, such as a reconnecting remote client,
/// applies every event here and reads <see cref="Snapshot"/>. Not thread
/// safe: the caller serializes <see cref="Apply"/> and <see cref="Snapshot"/>.
/// Reports addressed to one client (help, status, stats, tool lists, notes,
/// and errors) are not session state and are not kept. Approval passes and
/// compaction summaries are kept whatever the operator's display settings
/// are; those settings decide only what a front end draws.
/// </summary>
public sealed class SessionState
{
    private readonly List<ChatItem> _conversation = [];
    private readonly List<ApprovalPassed> _approvals = [];
    private readonly Dictionary<string, ChildSessionDocument> _children = new(StringComparer.Ordinal);
    private SessionChrome? _chrome;
    private SessionPreferences? _preferences;
    private IReadOnlyList<SlashCompletion> _commands = [];
    private IReadOnlyList<string> _history = [];
    private IReadOnlyList<string> _queue = [];
    private IReadOnlyList<TodoItem> _todos = [];
    private IReadOnlyList<StatusFieldValue> _statusFields = [];
    private SideQuestionSnapshot _side = SideQuestionSnapshot.Empty;
    private SessionActivity _activity = SessionActivity.Idle;
    private TokenUsage? _usage;
    private TokenUsage? _cumulative;
    private int _contextWindow;
    private int? _estimate;
    private bool _turnActive;
    private TurnFinished? _lastTurn;
    private RoundBuffer? _round;

    public void Apply(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        switch (sessionEvent)
        {
            case SessionStarted started:
                _chrome = started.Chrome;
                _contextWindow = started.Chrome.ContextWindow;
                break;
            case ChromeChanged changed:
                _chrome = changed.Chrome;
                _contextWindow = changed.Chrome.ContextWindow;
                if (!changed.Chrome.AgentsEnabled)
                {
                    _children.Clear();
                }

                break;
            case PreferencesChanged preferences:
                _preferences = preferences.Preferences;
                break;
            case SlashCommandsChanged commands:
                _commands = commands.Commands;
                break;
            case PromptHistoryLoaded history:
                _history = history.Entries;
                break;
            case ImageHistoryInvalidated:
                // The terminal composer drops the same entries.
                _history = [.. _history.Where(static entry => !entry.Contains(ImageMarkerText.Prefix))];
                break;
            case ApprovalPassed passed:
                _approvals.Add(passed);
                break;
            case CompactionFinished { Summary: { Length: > 0 } summary }:
                // The archive keeps the summary, so resume and fork replay show it.
                _conversation.Add(new ChatMessage(ChatRole.System, summary));
                break;
            case StatusFieldsChanged fields:
                _statusFields = fields.Fields;
                break;
            case ActivityChanged activity:
                _activity = activity.Activity;
                break;
            case QueueChanged queue:
                _queue = queue.Items;
                break;
            case TodosChanged todos:
                _todos = todos.Items;
                break;
            case SideQuestionSnapshot side:
                _side = side;
                break;
            case UsageChanged usage:
                _usage = usage.Usage;
                _cumulative = usage.CumulativeUsage;
                _contextWindow = usage.ContextWindow;
                if (!usage.Interim)
                {
                    _estimate = null;
                }

                break;
            case ContextEstimated estimated:
                _estimate = estimated.Tokens;
                break;
            case UserMessageSent sent:
                _conversation.Add(new ChatMessage(ChatRole.User, sent.Text));
                break;
            case UserMessageRecalled recalled:
                RemoveRecalled(recalled.Text);
                break;
            case TurnStarted:
                _turnActive = true;
                _round = new RoundBuffer();
                break;
            case StreamReceived stream:
                (_round ??= new RoundBuffer()).Apply(stream.StreamEvent);
                break;
            case RetryScheduled:
                _round = (_round ?? new RoundBuffer()).Retry();
                break;
            case ModelRoundClosed:
                CommitRound();
                break;
            case ToolCallsIssued calls:
                AddCalls(calls.Calls);
                break;
            case ToolResultsReceived results:
                _conversation.AddRange(results.Results);
                break;
            case TurnFinished finished:
                _turnActive = false;
                CommitRound();
                _lastTurn = finished;
                _usage = finished.Usage;
                _cumulative = finished.CumulativeUsage;
                _contextWindow = finished.ContextWindow;
                break;
            case ConversationCleared:
                _conversation.Clear();
                _approvals.Clear();
                _children.Clear();
                _round = null;
                _lastTurn = null;
                break;
            case HistoryReplayed replayed:
                _conversation.Clear();
                _conversation.AddRange(replayed.Items);
                _approvals.Clear();
                _children.Clear();
                _round = null;
                break;
            case ChildSessionChanged changed:
                _children[changed.Child.Id] = _children.TryGetValue(changed.Child.Id, out var known)
                    && ChildSessionFold.IsActive(changed.Child)
                    ? changed.Child with { Usage = known.Usage, CumulativeUsage = known.CumulativeUsage, Todos = known.Todos }
                    : changed.Child;
                break;
            case ChildEventReceived child when _children.TryGetValue(child.ChildSessionId, out var snapshot):
                _children[child.ChildSessionId] = ChildSessionFold.Apply(snapshot, child.Event);
                break;
            default:
                break;
        }
    }

    public SessionSnapshot Snapshot() =>
        new(
            _chrome,
            _preferences,
            _commands,
            _history,
            [.. _conversation],
            _round?.ToLive(),
            _turnActive,
            _activity,
            _queue,
            _todos,
            _statusFields,
            _usage,
            _cumulative,
            _contextWindow,
            _estimate,
            _side,
            [.. _children.Values],
            _lastTurn,
            [.. _approvals]);

    private void CommitRound()
    {
        if (_round is null)
        {
            return;
        }

        _conversation.AddRange(_round.CommittedItems());
        _round = _turnActive ? new RoundBuffer() : null;
    }

    /// <summary>
    /// The round already committed the streamed calls; issued calls carry the
    /// final arguments, so they replace the streamed copies.
    /// </summary>
    private void AddCalls(IReadOnlyList<ToolCall> calls)
    {
        var ids = calls.Select(static call => call.CallId).ToHashSet(StringComparer.Ordinal);
        _conversation.RemoveAll(item => item is ToolCall call && ids.Contains(call.CallId));
        _conversation.AddRange(calls);
    }

    private void RemoveRecalled(string text)
    {
        var index = _conversation.FindLastIndex(item =>
            item is ChatMessage message
            && message.Role == ChatRole.User
            && string.Equals(message.Text, text, StringComparison.Ordinal));
        if (index >= 0)
        {
            _conversation.RemoveAt(index);
        }
    }

    private sealed class RoundBuffer
    {
        private readonly StringBuilder _text = new();
        private readonly StringBuilder _reasoning = new();
        private readonly SortedDictionary<int, CallBuffer> _calls = [];
        private int _retries;

        public void Apply(ChatStreamEvent streamEvent)
        {
            switch (streamEvent)
            {
                case ChatTextDelta text when text.CandidateIndex == 0:
                    _text.Append(text.Text);
                    break;
                case ChatReasoningTextDelta reasoning when reasoning.CandidateIndex == 0:
                    _reasoning.Append(reasoning.Text);
                    break;
                case ChatToolCallDelta call when call.CandidateIndex == 0:
                    if (!_calls.TryGetValue(call.ItemIndex, out var buffer))
                    {
                        buffer = new CallBuffer(call.InputKind);
                        _calls[call.ItemIndex] = buffer;
                    }

                    buffer.Append(call);
                    break;
                default:
                    break;
            }
        }

        public RoundBuffer Retry() => new() { _retries = _retries + 1 };

        public LiveRound ToLive() =>
            new(_text.ToString(), _reasoning.ToString(), [.. Calls()], _retries);

        public IEnumerable<ChatItem> CommittedItems()
        {
            if (_reasoning.Length > 0)
            {
                yield return new ChatReasoningItem(new ReasoningContent(
                    [new ReasoningText(_reasoning.ToString(), ReasoningTextKind.Trace)]));
            }

            if (_text.Length > 0)
            {
                yield return new ChatMessage(ChatRole.Assistant, _text.ToString());
            }

            foreach (var call in Calls())
            {
                yield return call;
            }
        }

        private IEnumerable<ToolCall> Calls() =>
            _calls.Values
                .Where(static call => call.Id.Length > 0 && call.Name.Length > 0)
                .Select(static call => new ToolCall(call.Id, call.Name, call.Arguments, call.Kind));
    }

    private sealed class CallBuffer(ToolInputKind kind)
    {
        private readonly StringBuilder _id = new();
        private readonly StringBuilder _name = new();
        private readonly StringBuilder _arguments = new();

        public ToolInputKind Kind { get; } = kind;

        public string Id => _id.ToString();

        public string Name => _name.ToString();

        public string Arguments => _arguments.ToString();

        public void Append(ChatToolCallDelta delta)
        {
            _id.Append(delta.CallIdDelta);
            _name.Append(delta.NameDelta);
            _arguments.Append(delta.ArgumentsDelta);
        }
    }
}
