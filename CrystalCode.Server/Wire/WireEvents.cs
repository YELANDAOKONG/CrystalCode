using System.Text.Json;

using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Server.Wire;

/// <summary>
/// The wire type and payload for every <see cref="SessionEvent"/> and every
/// Crystal <see cref="ChatStreamEvent"/>. The parity test fails when the
/// engine or Crystal adds a type this map does not name.
/// </summary>
public static class WireEvents
{
    public static IReadOnlyDictionary<Type, string> SessionTypes { get; } = new Dictionary<Type, string>
    {
        [typeof(SessionStarted)] = "session.started",
        [typeof(ChromeChanged)] = "chrome.changed",
        [typeof(PreferencesChanged)] = "preferences.changed",
        [typeof(SessionOptionsChanged)] = "options.changed",
        [typeof(StatusFieldsChanged)] = "status-fields.changed",
        [typeof(SlashCommandsChanged)] = "commands.changed",
        [typeof(PromptHistoryLoaded)] = "prompt-history.loaded",
        [typeof(ImageHistoryInvalidated)] = "images.invalidated",
        [typeof(ActivityChanged)] = "activity.changed",
        [typeof(LoadingProgressed)] = "loading.progressed",
        [typeof(UserMessageSent)] = "message.sent",
        [typeof(UserMessageRecalled)] = "message.recalled",
        [typeof(TurnStarted)] = "turn.started",
        [typeof(TurnFinished)] = "turn.finished",
        [typeof(ModelRoundClosed)] = "round.closed",
        [typeof(StreamReceived)] = "stream",
        [typeof(ToolCallsIssued)] = "tools.called",
        [typeof(ToolResultsReceived)] = "tools.results",
        [typeof(RetryScheduled)] = "retry.scheduled",
        [typeof(UsageChanged)] = "usage.changed",
        [typeof(ContextEstimated)] = "context.estimated",
        [typeof(QueueChanged)] = "queue.changed",
        [typeof(TodosChanged)] = "todos.changed",
        [typeof(HistoryReplayed)] = "conversation.replayed",
        [typeof(ConversationCleared)] = "conversation.cleared",
        [typeof(CompactionStarted)] = "compaction.started",
        [typeof(CompactionFinished)] = "compaction.finished",
        [typeof(NoteWritten)] = "note",
        [typeof(ErrorWritten)] = "error",
        [typeof(HelpRequested)] = "report.help",
        [typeof(StatusReported)] = "report.status",
        [typeof(StatsReported)] = "report.stats",
        [typeof(ToolsListed)] = "report.tools",
        [typeof(TodosListed)] = "report.todos",
        [typeof(SideQuestionSnapshot)] = "side.changed",
        [typeof(ApprovalPassed)] = "approval.passed",
        [typeof(ApprovalReviewing)] = "approval.reviewing",
        [typeof(ChildSessionChanged)] = "child.changed",
        [typeof(ChildEventReceived)] = "child.event",
        [typeof(ChildViewRequested)] = "child.view"
    };

    public static IReadOnlyDictionary<Type, string> StreamTypes { get; } = new Dictionary<Type, string>
    {
        [typeof(ChatTextDelta)] = "stream.text",
        [typeof(ChatReasoningTextDelta)] = "stream.reasoning",
        [typeof(ChatReasoningStateReceived)] = "stream.reasoning-state",
        [typeof(ChatToolCallDelta)] = "stream.tool-call",
        [typeof(ChatCandidateCompleted)] = "stream.completed",
        [typeof(ChatUsageReceived)] = "stream.usage"
    };

    /// <summary>
    /// True for events that answer one client's request. They go only to the
    /// client named by <see cref="SessionEvent.Origin"/>; with no origin they
    /// go to everyone.
    /// </summary>
    public static bool IsReply(SessionEvent sessionEvent) => sessionEvent is NoteWritten
        or ErrorWritten
        or HelpRequested
        or StatusReported
        or StatsReported
        or ToolsListed
        or TodosListed
        or ChildViewRequested;

    /// <summary>The wire type, with child events unwrapped to their inner type.</summary>
    public static string Type(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        return sessionEvent switch
        {
            ChildEventReceived child => Type(child.Event),
            StreamReceived stream => StreamTypes.TryGetValue(stream.StreamEvent.GetType(), out var streamType)
                ? streamType
                : "stream.unknown",
            _ => SessionTypes.TryGetValue(sessionEvent.GetType(), out var type) ? type : "unknown"
        };
    }

    /// <summary>Writes the <c>data</c> object for <paramref name="sessionEvent"/>.</summary>
    public static void Data(Utf8JsonWriter writer, SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sessionEvent);
        writer.WriteStartObject("data");
        WriteFields(writer, sessionEvent is ChildEventReceived child ? child.Event : sessionEvent);
        writer.WriteEndObject();
    }

    private static void WriteFields(Utf8JsonWriter writer, SessionEvent sessionEvent)
    {
        switch (sessionEvent)
        {
            case SessionStarted started:
                WireWriter.Chrome(writer, "chrome", started.Chrome);
                break;
            case ChromeChanged changed:
                WireWriter.Chrome(writer, "chrome", changed.Chrome);
                break;
            case PreferencesChanged preferences:
                WireWriter.Preferences(writer, "preferences", preferences.Preferences);
                break;
            case SessionOptionsChanged options:
                WireWriter.Options(writer, "options", options.Options);
                break;
            case StatusFieldsChanged fields:
                writer.WriteStartArray("fields");
                foreach (var field in fields.Fields)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", field.Name);
                    writer.WriteString("text", field.Text);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            case SlashCommandsChanged commands:
                WireWriter.Commands(writer, "commands", commands.Commands);
                break;
            case PromptHistoryLoaded history:
                WireWriter.Strings(writer, "entries", history.Entries);
                break;
            case ActivityChanged activity:
                writer.WriteString("activity", ActivityName(activity.Activity));
                break;
            case LoadingProgressed loading:
                writer.WriteString("kind", LoadingKindName(loading.Kind));
                writer.WriteString("name", loading.Name);
                break;
            case UserMessageSent sent:
                WireWriter.Text(writer, "text", sent.Text);
                break;
            case UserMessageRecalled recalled:
                WireWriter.Text(writer, "text", recalled.Text);
                writer.WriteString("draft", recalled.Text);
                break;
            case TurnFinished finished:
                writer.WriteString("reason", finished.Result.StopReason.Value);
                writer.WriteNumber("modelCalls", finished.Result.ModelCallCount);
                writer.WriteNumber("toolCalls", finished.Result.ToolCallCount);
                WireWriter.Usage(writer, "turnUsage", finished.Result.AccumulatedUsage);
                WireWriter.Usage(writer, "usage", finished.Usage);
                WireWriter.Usage(writer, "cumulativeUsage", finished.CumulativeUsage);
                writer.WriteNumber("contextWindow", finished.ContextWindow);
                break;
            case StreamReceived stream:
                Stream(writer, stream.StreamEvent);
                break;
            case ToolCallsIssued calls:
                WireWriter.Calls(writer, "calls", calls.Calls);
                break;
            case ToolResultsReceived results:
                writer.WriteStartArray("results");
                foreach (var result in results.Results)
                {
                    writer.WriteStartObject();
                    WireWriter.Result(writer, result);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                break;
            case RetryScheduled retry:
                writer.WriteNumber("attempt", retry.Attempt.Attempt);
                writer.WriteString("message", retry.Attempt.Message);
                writer.WriteNumber("delayMilliseconds", (long)retry.Attempt.Delay.TotalMilliseconds);
                break;
            case UsageChanged usage:
                WireWriter.Usage(writer, "usage", usage.Usage);
                WireWriter.Usage(writer, "cumulativeUsage", usage.CumulativeUsage);
                writer.WriteNumber("contextWindow", usage.ContextWindow);
                writer.WriteBoolean("interim", usage.Interim);
                break;
            case ContextEstimated estimated:
                writer.WriteNumber("tokens", estimated.Tokens);
                break;
            case QueueChanged queue:
                writer.WriteStartArray("items");
                foreach (var item in queue.Items)
                {
                    writer.WriteStringValue(ImageMarkerText.Display(item));
                }

                writer.WriteEndArray();
                break;
            case TodosChanged todos:
                WireWriter.Todos(writer, "items", todos.Items);
                break;
            case TodosListed todos:
                WireWriter.Todos(writer, "items", todos.Items);
                break;
            case HistoryReplayed replayed:
                WireWriter.Items(writer, "items", replayed.Items);
                break;
            case CompactionStarted started:
                writer.WriteString("id", started.Id);
                writer.WriteBoolean("manual", started.Manual);
                break;
            case CompactionFinished finished:
                writer.WriteString("id", finished.Id);
                writer.WriteString("status", finished.Status);
                WireWriter.Optional(writer, "summary", finished.Summary);
                break;
            case NoteWritten note:
                writer.WriteString("text", note.Text);
                break;
            case ErrorWritten error:
                writer.WriteString("text", error.Text);
                break;
            case HelpRequested help:
                WireWriter.Commands(writer, "builtIn", BuiltInHelp());
                WireWriter.Commands(writer, "plugins", help.PluginCommands);
                break;
            case StatusReported status:
                writer.WriteBoolean("full", status.Full);
                WireWriter.Status(writer, "status", status.Status);
                break;
            case StatsReported stats:
                writer.WriteBoolean("allWorkspaces", stats.AllWorkspaces);
                WireWriter.Stats(writer, "report", stats.Report);
                break;
            case ToolsListed tools:
                WireWriter.ToolCatalog(writer, "report", tools.Report);
                break;
            case SideQuestionSnapshot side:
                WireWriter.Side(writer, "side", side);
                break;
            case ApprovalPassed passed:
                writer.WriteStartObject("call");
                WireWriter.Call(writer, passed.Call);
                writer.WriteEndObject();
                WireWriter.Classification(writer, "classification", passed.Classification);
                writer.WriteString("reason", passed.Reason.Value);
                WireWriter.Verdict(writer, "review", passed.Review);
                WireWriter.Verdict(writer, "extendedReview", passed.ExtendedReview);
                break;
            case ApprovalReviewing reviewing:
                writer.WriteStartObject("call");
                WireWriter.Call(writer, reviewing.Call);
                writer.WriteEndObject();
                writer.WriteString("stage", reviewing.Stage.Value);
                break;
            case ChildSessionChanged changed:
                writer.WritePropertyName("child");
                WireWriter.Child(writer, changed.Child, full: false);
                writer.WriteString("invocationId", changed.InvocationId);
                break;
            case ChildViewRequested view:
                WireWriter.Optional(writer, "childSessionId", view.ChildSessionId);
                break;
            default:
                break;
        }
    }

    private static void Stream(Utf8JsonWriter writer, ChatStreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case ChatTextDelta text:
                writer.WriteNumber("candidate", text.CandidateIndex);
                writer.WriteNumber("item", text.ItemIndex);
                writer.WriteString("role", text.Role.Value);
                writer.WriteString("delta", ImageMarkerText.Display(text.Text));
                break;
            case ChatReasoningTextDelta reasoning:
                writer.WriteNumber("candidate", reasoning.CandidateIndex);
                writer.WriteNumber("item", reasoning.ItemIndex);
                writer.WriteNumber("segment", reasoning.TextSegmentIndex);
                writer.WriteString("kind", reasoning.Kind.Value);
                writer.WriteString("delta", reasoning.Text);
                break;
            case ChatReasoningStateReceived state:
                // Opaque provider state stays on the server.
                writer.WriteNumber("candidate", state.CandidateIndex);
                writer.WriteNumber("item", state.ItemIndex);
                writer.WriteString("format", state.State.Format);
                break;
            case ChatToolCallDelta call:
                writer.WriteNumber("candidate", call.CandidateIndex);
                writer.WriteNumber("item", call.ItemIndex);
                writer.WriteString("callIdDelta", call.CallIdDelta);
                writer.WriteString("nameDelta", call.NameDelta);
                writer.WriteString("argumentsDelta", call.ArgumentsDelta);
                writer.WriteString("inputKind", call.InputKind.Value);
                break;
            case ChatCandidateCompleted completed:
                writer.WriteNumber("candidate", completed.CandidateIndex);
                writer.WriteString("finishReason", completed.FinishReason.Value);
                break;
            case ChatUsageReceived usage:
                WireWriter.Usage(writer, "usage", usage.Usage);
                break;
            default:
                break;
        }
    }

    public static string ActivityName(SessionActivity activity) => activity switch
    {
        SessionActivity.Idle => "idle",
        SessionActivity.LoadingTools => "loading_tools",
        SessionActivity.LoadingPlugins => "loading_plugins",
        SessionActivity.Compacting => "compacting",
        SessionActivity.WaitingForModel => "waiting_for_model",
        _ => "unknown"
    };

    public static string LoadingKindName(LoadingKind kind) => kind switch
    {
        LoadingKind.ToolSet => "tool_set",
        LoadingKind.Plugin => "plugin",
        LoadingKind.McpServer => "mcp_server",
        _ => "unknown"
    };

    private static IReadOnlyList<SlashCompletion> BuiltInHelp() =>
        [.. SlashCatalog.BuiltIn.Select(static spec => new SlashCompletion(spec.Name, spec.Help, [spec.Name, .. spec.Aliases]))];
}
