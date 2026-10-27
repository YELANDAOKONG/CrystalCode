using System.Text.Json;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// Child Agent activity. With the client opt-in
/// (<c>clientCapabilities._meta.crystal.subagents</c>), each child is a
/// restricted session announced by <c>subagent_update</c> on the parent
/// before any traffic under its id, and its events are mapped like the
/// parent's under that id. Without it, which is plain ACP, no child session id
/// is sent: the parent <c>task</c> tool call carries the child's status,
/// counts, and latest text.
/// </summary>
internal sealed class ChildUpdates
{
    private readonly Action<string, Action<Utf8JsonWriter>> _send;
    private readonly Func<string, SessionUpdates> _session;
    private readonly bool _subagents;
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionUpdates> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChildSessionDocument> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);

    // A child's calls between issue and result. Approval prompts carry no
    // child id, so ownership is recorded from the child's own events.
    private readonly Dictionary<ToolCall, string> _calls = [];

    /// <param name="send">Sends one update object under the given session id.</param>
    /// <param name="session">Creates the update mapper for one child session id.</param>
    /// <param name="subagents">The client opted into subagent sessions.</param>
    public ChildUpdates(
        Action<string, Action<Utf8JsonWriter>> send,
        Func<string, SessionUpdates> session,
        bool subagents)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(session);
        _send = send;
        _session = session;
        _subagents = subagents;
    }

    /// <summary>The children seen on this connection, latest record first by id.</summary>
    public IReadOnlyList<ChildSessionDocument> Known
    {
        get
        {
            lock (_gate)
            {
                return [.. _known.Values];
            }
        }
    }

    /// <summary>Applies one child event. <paramref name="parentId"/> is the ACP session id.</summary>
    public void Apply(SessionEvent sessionEvent, string parentId)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            switch (sessionEvent)
            {
                case ChildSessionChanged changed:
                    Changed(changed.Child, parentId);
                    break;
                case ChildEventReceived received:
                    Received(received, parentId);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Finds the child that issued <paramref name="call"/>. With subagent
    /// sessions, <paramref name="updates"/> is that child's mapper; otherwise null.
    /// </summary>
    public bool TryFind(ToolCall call, out ChildSessionDocument child, out SessionUpdates? updates)
    {
        ArgumentNullException.ThrowIfNull(call);
        lock (_gate)
        {
            updates = null;
            if (!_calls.TryGetValue(call, out var id) || !_known.TryGetValue(id, out child!))
            {
                child = null!;
                return false;
            }

            if (_subagents)
            {
                updates = Session(id);
            }

            return true;
        }
    }

    private void Changed(ChildSessionDocument child, string parentId)
    {
        var first = !_known.ContainsKey(child.Id);
        _known[child.Id] = child;
        if (_subagents)
        {
            _send(parentId, writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sessionUpdate", "subagent_update");
                writer.WriteString("sessionId", child.Id);
                if (first)
                {
                    writer.WriteString("title", child.Label);
                    writer.WriteString("description", child.Agent);
                }

                writer.WriteStartObject("state");
                writer.WriteString("state", State(child.Status));
                if (StopReason(child) is { } stop)
                {
                    writer.WriteString("stopReason", stop);
                }

                writer.WriteEndObject();
                Meta(writer, "ChildSessionChanged", child);
                writer.WriteEndObject();
            });
            return;
        }

        TaskCall(child, parentId, "ChildSessionChanged");
    }

    private void Received(ChildEventReceived received, string parentId)
    {
        if (!_known.TryGetValue(received.ChildSessionId, out var child))
        {
            // The announcement must come first; a child the engine never announced is dropped.
            return;
        }

        switch (received.Event)
        {
            case ToolCallsIssued issued:
                foreach (var call in issued.Calls)
                {
                    _calls[call] = child.Id;
                }

                break;
            case ToolResultsReceived results:
                foreach (var call in _calls.Keys
                    .Where(call => _calls[call] == child.Id && results.Results.Any(result => result.CallId == call.CallId))
                    .ToArray())
                {
                    _calls.Remove(call);
                }

                break;
            default:
                break;
        }

        if (_subagents)
        {
            Session(child.Id).Apply(received.Event);
            return;
        }

        switch (received.Event)
        {
            case StreamReceived { StreamEvent: ChatTextDelta text } when text.Text.Length > 0:
                _text[child.Id] = (_text.GetValueOrDefault(child.Id) ?? string.Empty) + text.Text;
                break;
            case ModelRoundClosed when _text.ContainsKey(child.Id):
                TaskCall(child, parentId, "ChildEventReceived");
                break;
            default:
                break;
        }
    }

    private SessionUpdates Session(string id)
    {
        if (!_children.TryGetValue(id, out var updates))
        {
            updates = _session(id);
            _children[id] = updates;
        }

        return updates;
    }

    /// <summary>The parent <c>task</c> call, updated with what the child is doing.</summary>
    private void TaskCall(ChildSessionDocument child, string parentId, string eventName)
    {
        if (child.ParentCallId.Length == 0)
        {
            return;
        }

        var latest = _text.GetValueOrDefault(child.Id);
        _send(parentId, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("sessionUpdate", "tool_call_update");
            writer.WriteString("toolCallId", child.ParentCallId);
            if (child.Status is "queued" or "running")
            {
                writer.WriteString("status", "in_progress");
            }

            if (latest is { Length: > 0 })
            {
                writer.WriteStartArray("content");
                writer.WriteStartObject();
                writer.WriteString("type", "content");
                writer.WriteStartObject("content");
                writer.WriteString("type", "text");
                writer.WriteString("text", latest);
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            Meta(writer, eventName, child);
            writer.WriteEndObject();
        });
    }

    private static void Meta(Utf8JsonWriter writer, string eventName, ChildSessionDocument child)
    {
        writer.WriteStartObject("_meta");
        writer.WriteStartObject("crystal");
        writer.WriteString("event", eventName);
        writer.WriteStartObject("child");
        writer.WriteString("childSessionId", child.Id);
        writer.WriteString("agent", child.Agent);
        writer.WriteString("label", child.Label);
        writer.WriteString("status", child.Status);
        writer.WriteNumber("modelCalls", child.ModelCalls);
        writer.WriteNumber("toolCalls", child.ToolCalls);
        CrystalWire.Todos(writer, "todos", Engine.Sessions.ChildSessionFold.Todos(child));
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string State(string status) => status is "queued" or "running" ? "running" : "idle";

    /// <summary>ACP stop reasons, plus the RFD's <c>error</c>, for a settled child.</summary>
    private static string? StopReason(ChildSessionDocument child) =>
        child.Status switch
        {
            "queued" or "running" => null,
            "interrupted" => "cancelled",
            "failed" => "error",
            _ when child.StopReason is { } stop
                && TurnWire.StopReason(new Engine.Sessions.TurnStopReason(stop)) is { } mapped => mapped,
            _ => "error"
        };
}
