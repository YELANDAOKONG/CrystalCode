using System.Text.Json;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// Maps engine events onto ACP <c>session/update</c> notifications. Every
/// update carries <c>_meta.crystal.event</c> with the engine event name.
/// Events with no ACP shape in this phase are listed in
/// <see cref="Unmapped"/> with the reason, so the parity test can tell a
/// deliberate gap from a forgotten one.
/// </summary>
internal sealed class SessionUpdates
{
    /// <summary>Engine events this phase does not send, and why.</summary>
    public static IReadOnlyDictionary<Type, string> Unmapped { get; } = new Dictionary<Type, string>
    {
        [typeof(SessionStarted)] = "Phase 2: session_info_update",
        [typeof(ChromeChanged)] = "Phase 2: config_option_update and current_mode_update",
        [typeof(PreferencesChanged)] = "Phase 2: config options",
        [typeof(StatusFieldsChanged)] = "Phase 4: _crystal/chrome",
        [typeof(SlashCommandsChanged)] = "Phase 2: available_commands_update",
        [typeof(PromptHistoryLoaded)] = "Phase 4: _crystal/promptHistory",
        [typeof(ImageHistoryInvalidated)] = "Phase 4: _crystal/imagesInvalidated",
        [typeof(ActivityChanged)] = "Phase 4: _crystal/activity",
        [typeof(UserMessageRecalled)] = "Phase 4: _crystal/recalled",
        [typeof(TurnStarted)] = "Ends in the session/prompt response, not an update",
        [typeof(TurnFinished)] = "Ends in the session/prompt response, not an update",
        [typeof(RetryScheduled)] = "Sent as a notice when the client supports notices",
        [typeof(UsageChanged)] = "Phase 2: usage_update",
        [typeof(ContextEstimated)] = "Phase 2: usage_update",
        [typeof(QueueChanged)] = "Phase 4: _crystal/queue",
        [typeof(HistoryReplayed)] = "Phase 2: session/load replay",
        [typeof(ConversationCleared)] = "Phase 2: _crystal/cleared",
        [typeof(HelpRequested)] = "Phase 2: report text",
        [typeof(StatusReported)] = "Phase 2: report text",
        [typeof(StatsReported)] = "Phase 2: report text",
        [typeof(ToolsListed)] = "Phase 2: report text",
        [typeof(TodosListed)] = "Phase 2: report text",
        [typeof(SideQuestionSnapshot)] = "Phase 4: _crystal/side",
        [typeof(ChildSessionChanged)] = "Phase 4: children",
        [typeof(ChildEventReceived)] = "Phase 4: children",
        [typeof(ChildViewRequested)] = "Phase 4: _crystal/childView"
    };

    /// <summary>Engine events this phase sends as session updates.</summary>
    public static IReadOnlyList<Type> Mapped { get; } =
    [
        typeof(UserMessageSent),
        typeof(StreamReceived),
        typeof(ModelRoundClosed),
        typeof(ToolCallsIssued),
        typeof(ToolResultsReceived),
        typeof(ApprovalReviewing),
        typeof(ApprovalPassed),
        typeof(TodosChanged),
        typeof(NoteWritten),
        typeof(ErrorWritten)
    ];

    private readonly Action<Action<Utf8JsonWriter>> _send;
    private readonly Func<string?> _workspace;
    private readonly bool _notices;
    private readonly object _gate = new();
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _streamingCalls = [];
    private int _round;
    private int _userMessages;

    /// <param name="send">Writes one <c>update</c> object; the caller wraps it in <c>session/update</c>.</param>
    /// <param name="workspace">The live workspace root, for locations and diffs.</param>
    /// <param name="notices">The client advertised <c>session.notices</c>.</param>
    public SessionUpdates(Action<Action<Utf8JsonWriter>> send, Func<string?> workspace, bool notices)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(workspace);
        _send = send;
        _workspace = workspace;
        _notices = notices;
    }

    public void Apply(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            switch (sessionEvent)
            {
                case UserMessageSent sent:
                    UserMessage(sent.Text);
                    break;
                case StreamReceived stream:
                    Stream(stream.StreamEvent);
                    break;
                case ModelRoundClosed:
                    _round++;
                    _streamingCalls.Clear();
                    break;
                case RetryScheduled retry:
                    Notice("warning", "Retrying the model request", $"Attempt {retry.Attempt.Attempt}: {retry.Attempt.Message}", "RetryScheduled");
                    _streamingCalls.Clear();
                    break;
                case ToolCallsIssued calls:
                    foreach (var call in calls.Calls)
                    {
                        Issue(call);
                    }

                    break;
                case ToolResultsReceived results:
                    foreach (var result in results.Results)
                    {
                        Result(result);
                    }

                    break;
                case ApprovalReviewing reviewing:
                    Approval(reviewing.Call, null, "ApprovalReviewing", writer =>
                    {
                        writer.WriteString("stage", reviewing.Stage.Value);
                        writer.WriteString("state", "reviewing");
                    });
                    break;
                case ApprovalPassed passed:
                    Approval(passed.Call, "in_progress", "ApprovalPassed", writer =>
                    {
                        writer.WriteString("state", "passed");
                        writer.WriteString("reason", passed.Reason.Value);
                        writer.WriteString("risk", passed.Classification.Risk.Value);
                        writer.WriteString("authority", passed.Classification.Authority.Value);
                        writer.WriteString("summary", passed.Classification.Summary);
                        Verdict(writer, "review", passed.Review);
                        Verdict(writer, "extendedReview", passed.ExtendedReview);
                    });
                    break;
                case TodosChanged todos:
                    Plan(todos.Items);
                    break;
                case NoteWritten note:
                    Notice("info", note.Text, null, "NoteWritten");
                    break;
                case ErrorWritten error:
                    if (_notices)
                    {
                        Notice("error", error.Text, null, "ErrorWritten");
                    }
                    else
                    {
                        Update("agent_message_chunk", "ErrorWritten", writer =>
                        {
                            TextContent(writer, error.Text);
                            writer.WriteString("messageId", "error-" + Guid.NewGuid().ToString("N"));
                        });
                    }

                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// A call the approval policy is about to ask about, announced before the
    /// permission request so the client already knows the call.
    /// </summary>
    public void EnsureAnnounced(ToolCall call)
    {
        lock (_gate)
        {
            Issue(call);
        }
    }

    private void UserMessage(string text)
    {
        var id = "user-" + (++_userMessages).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Update("user_message_chunk", "UserMessageSent", writer =>
        {
            TextContent(writer, ImageMarkerText.Display(text));
            writer.WriteString("messageId", id);
        });
    }

    private void Stream(ChatStreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case ChatTextDelta text when text.Text.Length > 0:
                Update("agent_message_chunk", "StreamReceived", writer =>
                {
                    TextContent(writer, ImageMarkerText.Display(text.Text));
                    writer.WriteString("messageId", MessageId("agent", text.CandidateIndex));
                });
                break;
            case ChatReasoningTextDelta reasoning when reasoning.Text.Length > 0:
                Update("agent_thought_chunk", "StreamReceived", writer =>
                {
                    TextContent(writer, reasoning.Text);
                    writer.WriteString("messageId", MessageId("thought", reasoning.CandidateIndex));
                });
                break;
            case ChatToolCallDelta call:
                StreamingCall(call);
                break;
            default:
                // Opaque reasoning state, usage, and completion carry nothing
                // a client shows in this phase. Opaque state never leaves.
                break;
        }
    }

    private void StreamingCall(ChatToolCallDelta delta)
    {
        if (delta.CandidateIndex != 0)
        {
            return;
        }

        _streamingCalls.TryGetValue(delta.ItemIndex, out var known);
        var callId = (known ?? string.Empty) + delta.CallIdDelta;
        _streamingCalls[delta.ItemIndex] = callId;
        if (known is null && callId.Length > 0 && delta.NameDelta.Length > 0 && _announced.Add(callId))
        {
            var name = delta.NameDelta;
            Update("tool_call", "StreamReceived", writer =>
            {
                writer.WriteString("toolCallId", callId);
                writer.WriteString("title", ToolCallSummary.Text(name, string.Empty));
                writer.WriteString("kind", "other");
                writer.WriteString("status", "pending");
                writer.WriteString("name", name);
            });
        }
    }

    private void Issue(ToolCall call)
    {
        var known = !_announced.Add(call.CallId);
        var workspace = _workspace();
        Update(known ? "tool_call_update" : "tool_call", "ToolCallsIssued", writer =>
        {
            writer.WriteString("toolCallId", call.CallId);
            writer.WriteString("name", call.Name);
            if (!known)
            {
                writer.WriteString("status", "pending");
            }

            ToolCallWire.Describe(writer, call, workspace);
        });
    }

    private void Result(ToolResult result)
    {
        if (!_announced.Contains(result.CallId))
        {
            return;
        }

        Update("tool_call_update", "ToolResultsReceived", writer =>
        {
            writer.WriteString("toolCallId", result.CallId);
            ToolCallWire.Result(writer, result);
        });
        _announced.Remove(result.CallId);
    }

    private void Approval(ToolCall call, string? status, string eventName, Action<Utf8JsonWriter> approval)
    {
        if (!_announced.Contains(call.CallId))
        {
            Issue(call);
        }

        Update("tool_call_update", eventName, writer =>
        {
            writer.WriteString("toolCallId", call.CallId);
            JsonText.Optional(writer, "status", status);
        }, crystal =>
        {
            crystal.WriteStartObject("approval");
            approval(crystal);
            crystal.WriteEndObject();
        });
    }

    private void Plan(IReadOnlyList<TodoItem> items)
    {
        Update("plan", "TodosChanged", writer =>
        {
            writer.WriteStartArray("entries");
            foreach (var item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("content", item.Content);
                writer.WriteString("priority", "medium");
                writer.WriteString("status", item.Status switch
                {
                    TodoStatus.Pending => "pending",
                    TodoStatus.InProgress => "in_progress",
                    _ => "completed"
                });
                writer.WriteStartObject("_meta");
                writer.WriteStartObject("crystal");
                writer.WriteString("id", item.Id);
                if (item.Status == TodoStatus.Cancelled)
                {
                    writer.WriteString("status", "cancelled");
                }

                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    /// <summary>Notes and retries are notices; a client without notices does not get them.</summary>
    private void Notice(string severity, string title, string? description, string eventName)
    {
        if (!_notices || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        Update("notice", eventName, writer =>
        {
            writer.WriteString("severity", severity);
            writer.WriteString("title", title);
            JsonText.Optional(writer, "description", description);
        });
    }

    private string MessageId(string kind, int candidate) =>
        $"{kind}-{_round}-{candidate}";

    private void Update(
        string kind,
        string eventName,
        Action<Utf8JsonWriter> fields,
        Action<Utf8JsonWriter>? crystal = null)
    {
        _send(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("sessionUpdate", kind);
            fields(writer);
            writer.WriteStartObject("_meta");
            writer.WriteStartObject("crystal");
            writer.WriteString("event", eventName);
            crystal?.Invoke(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    private static void TextContent(Utf8JsonWriter writer, string text)
    {
        writer.WriteStartObject("content");
        writer.WriteString("type", "text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
    }

    private static void Verdict(Utf8JsonWriter writer, string name, Engine.Approvals.ApprovalReviewVerdict? verdict)
    {
        if (verdict is null)
        {
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("outcome", verdict.Outcome);
        writer.WriteString("riskLevel", verdict.RiskLevel.Value);
        writer.WriteString("userAuthorization", verdict.UserAuthorization.Value);
        writer.WriteString("rationale", verdict.Rationale);
        writer.WriteEndObject();
    }
}
