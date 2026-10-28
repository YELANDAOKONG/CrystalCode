using System.Globalization;
using System.Text.Json;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// Maps engine events onto ACP <c>session/update</c> notifications. Every
/// update carries <c>_meta.crystal.event</c> with the engine event name.
/// Engine state with no ACP shape goes out as a <c>_crystal/*</c>
/// notification; <see cref="Unmapped"/> lists the events that are neither,
/// with the reason, so the parity test can tell a deliberate gap from a
/// forgotten one. Child events go to <see cref="ChildUpdates"/>.
/// </summary>
internal sealed class SessionUpdates
{
    /// <summary>Engine events Bridge does not send, and why.</summary>
    public static IReadOnlyDictionary<Type, string> Unmapped { get; } = new Dictionary<Type, string>
    {
        [typeof(UserMessageSent)] = "The client shows the prompt it sent; session/load replays it from the archive",
        [typeof(TurnStarted)] = "Ends in the session/prompt response, not an update",
        [typeof(TurnFinished)] = "Ends in the session/prompt response, not an update"
    };

    /// <summary>Engine events Bridge sends as session updates.</summary>
    public static IReadOnlyList<Type> Mapped { get; } =
    [
        typeof(SessionStarted),
        typeof(ChromeChanged),
        typeof(PreferencesChanged),
        typeof(SessionOptionsChanged),
        typeof(SlashCommandsChanged),
        typeof(StreamReceived),
        typeof(ModelRoundClosed),
        typeof(RetryScheduled),
        typeof(UsageChanged),
        typeof(ContextEstimated),
        typeof(ToolCallsIssued),
        typeof(ToolResultsReceived),
        typeof(ApprovalReviewing),
        typeof(ApprovalPassed),
        typeof(TodosChanged),
        typeof(HistoryReplayed),
        typeof(ConversationCleared),
        typeof(HelpRequested),
        typeof(StatusReported),
        typeof(StatsReported),
        typeof(ToolsListed),
        typeof(TodosListed),
        typeof(NoteWritten),
        typeof(ErrorWritten),
        typeof(CompactionStarted),
        typeof(CompactionFinished),
        typeof(ChildSessionChanged),
        typeof(ChildEventReceived)
    ];

    /// <summary>Engine events Bridge sends only as <c>_crystal/*</c> notifications.</summary>
    public static IReadOnlyList<Type> Notified { get; } =
    [
        typeof(StatusFieldsChanged),
        typeof(PromptHistoryLoaded),
        typeof(ImageHistoryInvalidated),
        typeof(ActivityChanged),
        typeof(LoadingProgressed),
        typeof(UserMessageRecalled),
        typeof(QueueChanged),
        typeof(SideQuestionSnapshot),
        typeof(ChildViewRequested)
    ];

    /// <summary>Every Crystal stream event, and what Bridge does with it.</summary>
    public static IReadOnlyDictionary<Type, string> StreamEvents { get; } = new Dictionary<Type, string>
    {
        [typeof(ChatTextDelta)] = "agent_message_chunk",
        [typeof(ChatReasoningTextDelta)] = "agent_thought_chunk",
        [typeof(ChatToolCallDelta)] = "tool_call on first sight",
        [typeof(ChatReasoningStateReceived)] = "Dropped: opaque reasoning state never leaves the process",
        [typeof(ChatUsageReceived)] = "UsageChanged carries the totals as usage_update",
        [typeof(ChatCandidateCompleted)] = "No update; the next round gets a new messageId"
    };

    private readonly Action<Action<Utf8JsonWriter>> _send;
    private readonly Func<string?> _workspace;
    private readonly bool _notices;
    private readonly bool _booleans;
    private readonly bool _compaction;
    private readonly Action<string, Action<Utf8JsonWriter>> _crystal;
    private readonly Func<int, (string MimeType, byte[] Data)?> _image;
    private readonly object _gate = new();
    private readonly HashSet<string> _announced = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _streamingCalls = [];
    private int _round;
    private int _reports;
    private int _replays;
    private readonly IReadOnlySet<string>? _offered;
    private IReadOnlyList<SessionOption> _options = [];
    private string? _mode;
    private int _contextWindow;
    private string? _reportedWorkspace;
    private string _engineSessionId = string.Empty;

    /// <param name="send">Writes one <c>update</c> object; the caller wraps it in <c>session/update</c>.</param>
    /// <param name="workspace">The live workspace root, for locations and diffs.</param>
    /// <param name="notices">The client advertised <c>session.notices</c>.</param>
    /// <param name="booleans">The client advertised boolean config options.</param>
    /// <param name="image">The MIME type and bytes of a session image, for replay.</param>
    /// <param name="compaction">The client advertised <c>session.compaction</c>.</param>
    /// <param name="crystal">Sends one <c>_crystal/*</c> notification: method, then its fields after <c>sessionId</c>.</param>
    /// <param name="offered">The option ids the client sees, or null for every option.</param>
    public SessionUpdates(
        Action<Action<Utf8JsonWriter>> send,
        Func<string?> workspace,
        bool notices,
        bool booleans = false,
        Func<int, (string MimeType, byte[] Data)?>? image = null,
        bool compaction = false,
        Action<string, Action<Utf8JsonWriter>>? crystal = null,
        IReadOnlySet<string>? offered = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(workspace);
        _send = send;
        _workspace = workspace;
        _notices = notices;
        _booleans = booleans;
        _compaction = compaction;
        _offered = offered;
        _crystal = crystal ?? (static (_, _) => { });
        _image = image ?? (static _ => null);
    }

    /// <summary>The last option catalog the engine published, whether or not the client sees it.</summary>
    public IReadOnlyList<SessionOption> Options
    {
        get
        {
            lock (_gate)
            {
                return _options;
            }
        }
    }

    /// <summary>The published options the client sees; a hidden one cannot be set through ACP.</summary>
    public IReadOnlyList<SessionOption> OfferedOptions
    {
        get
        {
            lock (_gate)
            {
                return Offered(_options);
            }
        }
    }

    /// <summary>The engine session id; it changes on <c>/clear</c>, the ACP id does not.</summary>
    public string EngineSessionId
    {
        get
        {
            lock (_gate)
            {
                return _engineSessionId;
            }
        }
        set
        {
            lock (_gate)
            {
                _engineSessionId = value;
            }
        }
    }

    public void Apply(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            switch (sessionEvent)
            {
                case SessionOptionsChanged options:
                    OptionsChanged(options.Options);
                    break;
                case SlashCommandsChanged commands:
                    Commands(commands.Commands);
                    break;
                case SessionStarted started:
                    _reportedWorkspace = started.Chrome.WorkspaceRoot;
                    RememberContextWindow(started.Chrome.ContextWindow);
                    Chrome(started.Chrome);
                    break;
                case ChromeChanged changed when changed.Chrome.WorkspaceRoot != _reportedWorkspace:
                    // /cd and /space move the session; ACP's cwd is otherwise fixed.
                    _reportedWorkspace = changed.Chrome.WorkspaceRoot;
                    RememberContextWindow(changed.Chrome.ContextWindow);
                    SessionInfo("ChromeChanged", title: null);
                    Chrome(changed.Chrome);
                    break;
                case ChromeChanged changed:
                    // The option catalog carries the values; this keeps the plain chrome.
                    RememberContextWindow(changed.Chrome.ContextWindow);
                    Chrome(changed.Chrome);
                    break;
                case PreferencesChanged preferences:
                    _crystal("_crystal/preferences", writer => CrystalWire.Preferences(writer, "preferences", preferences.Preferences));
                    break;
                case StatusFieldsChanged fields:
                    _crystal("_crystal/statusFields", writer => CrystalWire.StatusFields(writer, "fields", fields.Fields));
                    break;
                case PromptHistoryLoaded history:
                    _crystal("_crystal/promptHistory", writer => CrystalWire.Strings(writer, "entries", history.Entries));
                    break;
                case ImageHistoryInvalidated:
                    _crystal("_crystal/imagesInvalidated", static _ => { });
                    break;
                case ActivityChanged activity:
                    _crystal("_crystal/activity", writer => writer.WriteString("activity", CrystalWire.ActivityName(activity.Activity)));
                    break;
                case LoadingProgressed loading:
                    _crystal("_crystal/loading", writer =>
                    {
                        writer.WriteString("kind", CrystalWire.LoadingKindName(loading.Kind));
                        writer.WriteString("name", loading.Name);
                    });
                    break;
                case UserMessageRecalled recalled:
                    // The draft keeps trusted markers so the client can submit it again as is.
                    _crystal("_crystal/recalled", writer =>
                    {
                        CrystalWire.Text(writer, "text", recalled.Text);
                        writer.WriteString("draft", recalled.Text);
                    });
                    break;
                case QueueChanged queue:
                    _crystal("_crystal/queue", writer => CrystalWire.Strings(writer, "items", queue.Items.Select(ImageMarkerText.Display)));
                    break;
                case SideQuestionSnapshot side:
                    _crystal("_crystal/side", writer => CrystalWire.Side(writer, "side", side));
                    break;
                case ChildViewRequested view:
                    _crystal("_crystal/childView", writer => CrystalWire.Optional(writer, "childSessionId", view.ChildSessionId));
                    break;
                case CompactionStarted started:
                    CompactionStart(started);
                    break;
                case CompactionFinished finished:
                    CompactionEnd(finished);
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
                case UsageChanged usage:
                    Usage(usage);
                    break;
                case ContextEstimated estimated:
                    Update("usage_update", "ContextEstimated", writer =>
                    {
                        writer.WriteNumber("used", Math.Max(0, estimated.Tokens));
                        writer.WriteNumber("size", Math.Max(0, CurrentContextWindow()));
                    }, crystal => crystal.WriteBoolean("estimate", true));
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
                case HistoryReplayed replayed:
                    Replay(replayed.Items);
                    break;
                case ConversationCleared:
                    _announced.Clear();
                    SessionInfo("ConversationCleared", title: null);
                    break;
                case HelpRequested help:
                    Report("help", "HelpRequested", HelpText.Format(help.PluginCommands),
                        writer => CrystalWire.Commands(writer, "plugins", help.PluginCommands));
                    break;
                case StatusReported status:
                    Report("status", "StatusReported", StatusText.Format(status.Status, status.Full), writer =>
                    {
                        writer.WriteBoolean("full", status.Full);
                        CrystalWire.Status(writer, "status", status.Status);
                    });
                    break;
                case StatsReported stats:
                    Report("stats", "StatsReported", StatsText.Format(stats.Report, stats.AllWorkspaces),
                        writer => CrystalWire.Stats(writer, "report", stats.Report, stats.AllWorkspaces));
                    break;
                case ToolsListed tools:
                    Report("tools", "ToolsListed", ToolListText.Format(tools.Report),
                        writer => CrystalWire.ToolCatalog(writer, "report", tools.Report));
                    break;
                case TodosListed todos:
                    Report("todos", "TodosListed", TodoList.Format(todos.Items),
                        writer => CrystalWire.Todos(writer, "items", todos.Items));
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

    /// <summary>A prompt block Bridge turned into a mention; sent only to a client with notices.</summary>
    public void Warn(string text)
    {
        lock (_gate)
        {
            Notice("warning", text, null, null);
        }
    }

    /// <summary>Title and time after the first user message and each turn.</summary>
    public void Saved(string title)
    {
        lock (_gate)
        {
            SessionInfo("TurnFinished", title);
        }
    }

    /// <summary>The <c>configOptions</c> and <c>modes</c> fields of a session response.</summary>
    public void WriteConfiguration(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        lock (_gate)
        {
            var offered = Offered(_options);
            ConfigWire.Options(writer, "configOptions", offered, _booleans);
            ConfigWire.Modes(writer, offered);
        }
    }

    private IReadOnlyList<SessionOption> Offered(IReadOnlyList<SessionOption> options) =>
        _offered is null ? options : [.. options.Where(option => _offered.Contains(option.Id))];

    private void OptionsChanged(IReadOnlyList<SessionOption> options)
    {
        _options = options;
        var offered = Offered(options);
        Update("config_option_update", "SessionOptionsChanged", writer =>
            ConfigWire.Options(writer, "configOptions", offered, _booleans));
        var mode = offered.FirstOrDefault(static option => option.Id == "mode")?.Current;
        if (mode is not null && mode != _mode)
        {
            _mode = mode;
            Update("current_mode_update", "SessionOptionsChanged", writer => writer.WriteString("currentModeId", mode));
        }
    }

    /// <summary>
    /// The completable verbs. ACP names a command without the slash; the hint
    /// lists the first argument keys, and the full tree rides in <c>_meta</c>.
    /// </summary>
    private void Commands(IReadOnlyList<SlashCompletion> commands)
    {
        Update("available_commands_update", "SlashCommandsChanged", writer =>
        {
            writer.WriteStartArray("availableCommands");
            foreach (var command in commands)
            {
                writer.WriteStartObject();
                writer.WriteString("name", command.Name);
                writer.WriteString("description", string.IsNullOrWhiteSpace(command.Help) ? command.Name : command.Help);
                if (command.ArgumentOptions.Count > 0)
                {
                    var keys = command.ArgumentOptions.Select(static argument => argument.Name).Take(6).ToArray();
                    writer.WriteStartObject("input");
                    writer.WriteString(
                        "hint",
                        string.Join(" | ", keys) + (command.ArgumentOptions.Count > keys.Length ? " | ..." : string.Empty));
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }, crystal => CrystalWire.Commands(crystal, "arguments", commands));
    }

    private void Usage(UsageChanged usage)
    {
        if (usage.Usage is null)
        {
            return;
        }

        RememberContextWindow(usage.ContextWindow);
        Update("usage_update", "UsageChanged", writer =>
        {
            writer.WriteNumber("used", usage.Usage.TotalTokenCount);
            writer.WriteNumber("size", Math.Max(0, usage.ContextWindow));
        }, crystal =>
        {
            crystal.WriteBoolean("interim", usage.Interim);
            TurnWire.Usage(crystal, "usage", usage.Usage);
            TurnWire.Usage(crystal, "cumulativeUsage", usage.CumulativeUsage);
        });
    }

    // The "context" option exists only for models with several selectable windows,
    // so the window is tracked from chrome and usage events instead.
    private int CurrentContextWindow() => _contextWindow;

    private void RememberContextWindow(int window)
    {
        if (window > 0)
        {
            _contextWindow = window;
        }
    }

    private void SessionInfo(string eventName, string? title)
    {
        var engineId = _engineSessionId;
        Update("session_info_update", eventName, writer =>
        {
            if (title is not null)
            {
                writer.WriteString("title", title);
            }

            writer.WriteString("updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }, crystal =>
        {
            JsonText.Optional(crystal, "engineSessionId", engineId.Length == 0 ? null : engineId);
            JsonText.Optional(crystal, "workspace", _workspace());
        });
    }

    /// <summary>
    /// A report is agent message text from the engine's plain-text renderer,
    /// with the kind under <c>_meta.crystal.report</c>; <c>_crystal/report</c>
    /// carries the structured values.
    /// </summary>
    private void Report(string kind, string eventName, string text, Action<Utf8JsonWriter>? values = null)
    {
        var id = "report-" + (++_reports).ToString(CultureInfo.InvariantCulture);
        Update("agent_message_chunk", eventName, writer =>
        {
            TextContent(writer, text);
            writer.WriteString("messageId", id);
        }, crystal => crystal.WriteString("report", kind));
        if (values is not null)
        {
            _crystal("_crystal/report", writer =>
            {
                writer.WriteString("kind", kind);
                values(writer);
            });
        }
    }

    private void Chrome(SessionChrome chrome) =>
        _crystal("_crystal/chrome", writer => CrystalWire.Chrome(writer, "chrome", chrome));

    /// <summary>
    /// With <c>session.compaction</c>, ACP's compaction entity. Without it a
    /// shown summary becomes agent text, as the terminal prints it.
    /// </summary>
    private void CompactionStart(CompactionStarted started)
    {
        if (!_compaction)
        {
            return;
        }

        Update("compaction_update", "CompactionStarted", writer =>
        {
            writer.WriteString("compactionId", started.Id);
            writer.WriteString("status", "in_progress");
        }, crystal => crystal.WriteBoolean("manual", started.Manual));
    }

    private void CompactionEnd(CompactionFinished finished)
    {
        if (!_compaction)
        {
            if (finished.Summary is { Length: > 0 } shown)
            {
                Update("agent_message_chunk", "CompactionFinished", writer =>
                {
                    TextContent(writer, shown);
                    writer.WriteString("messageId", finished.Id);
                });
            }

            return;
        }

        if (finished.Summary is { Length: > 0 } summary)
        {
            Update("compaction_summary_chunk", "CompactionFinished", writer =>
            {
                writer.WriteString("compactionId", finished.Id);
                TextContent(writer, summary);
            });
        }

        // "unchanged" found nothing to fold: ACP's nearest status is completed.
        Update("compaction_update", "CompactionFinished", writer =>
        {
            writer.WriteString("compactionId", finished.Id);
            writer.WriteString("status", finished.Status == "unchanged" ? "completed" : finished.Status);
        }, crystal => crystal.WriteString("status", finished.Status));
    }

    /// <summary>
    /// The saved conversation in order: user and assistant text, readable
    /// reasoning, tool calls with their results, and each compaction summary.
    /// Opaque reasoning state is never sent.
    /// </summary>
    private void Replay(IReadOnlyList<ChatItem> items)
    {
        var replay = ++_replays;
        var message = 0;
        _announced.Clear();
        foreach (var item in items)
        {
            message++;
            var id = $"replay-{replay}-{message}";
            switch (item)
            {
                case ChatMessage summary when CompactionSelection.IsSummary(summary):
                    if (_compaction)
                    {
                        Update("compaction_update", "HistoryReplayed", writer =>
                        {
                            writer.WriteString("compactionId", id);
                            writer.WriteString("status", "completed");
                            writer.WriteStartArray("summary");
                            writer.WriteStartObject();
                            writer.WriteString("type", "text");
                            writer.WriteString("text", summary.Text);
                            writer.WriteEndObject();
                            writer.WriteEndArray();
                        });
                        break;
                    }

                    Update("agent_message_chunk", "HistoryReplayed", writer =>
                    {
                        TextContent(writer, summary.Text);
                        writer.WriteString("messageId", id);
                    }, crystal => crystal.WriteString("replay", "compaction"));
                    break;
                case ChatMessage { Role.Value: "user" } user:
                    UserReplay(user.Text, id);
                    break;
                case ChatMessage { Role.Value: "assistant" } assistant when assistant.Text.Length > 0:
                    Update("agent_message_chunk", "HistoryReplayed", writer =>
                    {
                        TextContent(writer, ImageMarkerText.Display(assistant.Text));
                        writer.WriteString("messageId", id);
                    });
                    break;
                case ChatReasoningItem reasoning:
                    var text = string.Join("\n", reasoning.Content.TextSegments.Select(static segment => segment.Text));
                    if (text.Length > 0)
                    {
                        Update("agent_thought_chunk", "HistoryReplayed", writer =>
                        {
                            TextContent(writer, text);
                            writer.WriteString("messageId", id);
                        });
                    }

                    break;
                case ToolCall call:
                    _announced.Remove(call.CallId);
                    Issue(call, "HistoryReplayed");
                    break;
                case ToolResult result:
                    Result(result, "HistoryReplayed");
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>User text with each trusted image marker as an image block in place.</summary>
    private void UserReplay(string text, string id)
    {
        var position = 0;
        foreach (System.Text.RegularExpressions.Match match in ImageMarkerText.Matches(text))
        {
            if (match.Index > position)
            {
                var before = text[position..match.Index];
                Update("user_message_chunk", "HistoryReplayed", writer =>
                {
                    TextContent(writer, before);
                    writer.WriteString("messageId", id);
                });
            }

            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (_image(number) is { } image)
            {
                Update("user_message_chunk", "HistoryReplayed", writer =>
                {
                    writer.WriteStartObject("content");
                    writer.WriteString("type", "image");
                    writer.WriteString("mimeType", image.MimeType);
                    writer.WriteBase64String("data", image.Data);
                    writer.WriteEndObject();
                    writer.WriteString("messageId", id);
                }, crystal => crystal.WriteNumber("image", number));
            }
            else
            {
                var marker = match.Value[1..];
                Update("user_message_chunk", "HistoryReplayed", writer =>
                {
                    TextContent(writer, marker);
                    writer.WriteString("messageId", id);
                });
            }

            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            var rest = text[position..];
            Update("user_message_chunk", "HistoryReplayed", writer =>
            {
                TextContent(writer, rest);
                writer.WriteString("messageId", id);
            });
        }
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
                // Opaque reasoning state never leaves; usage arrives as
                // UsageChanged; completion only closes the message id.
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
                writer.WriteString("kind", ToolCallWire.Kind(new ToolCall(callId, name, string.Empty, delta.InputKind)));
                writer.WriteString("status", "pending");
                writer.WriteString("name", name);
            });
        }
    }

    private void Issue(ToolCall call, string eventName = "ToolCallsIssued")
    {
        var known = !_announced.Add(call.CallId);
        var workspace = eventName == "HistoryReplayed" ? null : _workspace();
        Update(known ? "tool_call_update" : "tool_call", eventName, writer =>
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

    private void Result(ToolResult result, string eventName = "ToolResultsReceived")
    {
        if (!_announced.Contains(result.CallId))
        {
            return;
        }

        Update("tool_call_update", eventName, writer =>
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
    private void Notice(string severity, string title, string? description, string? eventName)
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
        string? eventName,
        Action<Utf8JsonWriter> fields,
        Action<Utf8JsonWriter>? crystal = null)
    {
        _send(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("sessionUpdate", kind);
            fields(writer);
            if (eventName is null && crystal is null)
            {
                writer.WriteEndObject();
                return;
            }

            writer.WriteStartObject("_meta");
            writer.WriteStartObject("crystal");
            JsonText.Optional(writer, "event", eventName);
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
