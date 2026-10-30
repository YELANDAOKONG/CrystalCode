using System.Globalization;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// Plain Crystal values for <c>_crystal/*</c> results and notifications,
/// written field by field with the same names as <c>crystal server</c> uses,
/// so a client that knows one surface reads the other. Opaque reasoning
/// state, image bytes, system prompts, and settings objects are never written.
/// </summary>
internal static class CrystalWire
{
    public static void Usage(Utf8JsonWriter writer, string name, TokenUsage? usage)
    {
        if (usage is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("inputTokens", usage.InputTokenCount);
        writer.WriteNumber("outputTokens", usage.OutputTokenCount);
        if (usage.ReasoningTokenCount is { } reasoning)
        {
            writer.WriteNumber("reasoningTokens", reasoning);
        }

        writer.WriteNumber("totalTokens", usage.TotalTokenCount);
        writer.WriteEndObject();
    }

    public static void Chrome(Utf8JsonWriter writer, string name, SessionChrome? chrome)
    {
        if (chrome is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("provider", chrome.Provider);
        writer.WriteString("sessionId", chrome.SessionId);
        writer.WriteString("model", chrome.Model);
        writer.WriteString("workspace", chrome.WorkspaceRoot);
        writer.WriteString("mode", chrome.Mode ?? (chrome.PlanMode ? "plan" : "work"));
        writer.WriteBoolean("planMode", chrome.PlanMode);
        writer.WriteString("approval", chrome.Approval.Value);
        writer.WriteString("thinking", chrome.Thinking);
        writer.WriteString("promptSet", chrome.PromptSet);
        writer.WriteNumber("contextWindow", chrome.ContextWindow);
        writer.WriteString("serviceTier", chrome.ServiceTier);
        writer.WriteString("speed", chrome.Speed);
        writer.WriteBoolean("agentsEnabled", chrome.AgentsEnabled);
        Pending(writer, "pending", chrome.Pending);
        Reviewer(writer, "reviewer", chrome.Reviewer);
        ExtendedApproval(writer, "extendedApproval", chrome.ExtendedApproval);
        SideModel(writer, "compaction", chrome.Compaction);
        SideModel(writer, "image", chrome.Image);
        SideModel(writer, "audio", chrome.Audio);
        SideModel(writer, "video", chrome.Video);
        writer.WriteString("topic", chrome.Topic);
        writer.WriteEndObject();
    }

    private static void SideModel(Utf8JsonWriter writer, string name, SideModelChrome? model)
    {
        if (model is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("provider", model.Provider);
        writer.WriteString("model", model.Model);
        writer.WriteString("thinking", model.Thinking);
        writer.WriteString("serviceTier", model.ServiceTier);
        writer.WriteString("speed", model.Speed);
        writer.WriteString("context", model.Context);
        writer.WriteEndObject();
    }

    private static void Reviewer(Utf8JsonWriter writer, string name, ReviewerChrome? reviewer)
    {
        if (reviewer is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        ReviewerFields(writer, reviewer);
        writer.WriteEndObject();
    }

    private static void ReviewerFields(Utf8JsonWriter writer, ReviewerChrome reviewer)
    {
        writer.WriteString("provider", reviewer.Provider);
        writer.WriteString("model", reviewer.Model);
        writer.WriteString("thinking", reviewer.Thinking);
        writer.WriteString("serviceTier", reviewer.ServiceTier);
        writer.WriteString("speed", reviewer.Speed);
        writer.WriteString("context", reviewer.Context);
    }

    private static void ExtendedApproval(Utf8JsonWriter writer, string name, ExtendedApprovalChrome? extended)
    {
        if (extended is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        ReviewerFields(writer, extended.Reviewer);
        writer.WriteString("trigger", extended.Trigger);
        writer.WriteBoolean("includePrimaryReview", extended.IncludePrimaryReview);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Settings chosen during a turn that apply on the next turn. The object
    /// is always present; a key appears only for a setting that waits. A
    /// pending tier or speed of <c>unset</c> clears that slot.
    /// </summary>
    public static void Pending(Utf8JsonWriter writer, string name, SessionPendingSettings? pending)
    {
        writer.WriteStartObject(name);
        if (pending is not null)
        {
            if (pending.Provider is { } provider && pending.Model is { } model)
            {
                writer.WriteString("provider", provider);
                writer.WriteString("model", model);
            }

            if (pending.ContextWindow is int window)
            {
                writer.WriteNumber("contextWindow", window);
            }

            PendingValue(writer, "serviceTier", pending.ServiceTier);
            PendingValue(writer, "speed", pending.Speed);
            PendingValue(writer, "thinking", pending.Thinking);
            PendingValue(writer, "mode", pending.Mode);
            PendingValue(writer, "approval", pending.Approval);
        }

        writer.WriteEndObject();
    }

    private static void PendingValue(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    public static void Preferences(Utf8JsonWriter writer, string name, SessionPreferences? preferences)
    {
        if (preferences is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteBoolean("estimatedTokens", preferences.EstimatedTokens);
        writer.WriteBoolean("todoBar", preferences.TodoBar);
        writer.WriteNumber("todoBarRows", preferences.TodoBarRows);
        writer.WriteBoolean("verboseTools", preferences.VerboseTools);
        writer.WriteBoolean("verboseCommands", preferences.VerboseCommands);
        writer.WriteBoolean("verboseResults", preferences.VerboseResults);
        writer.WriteBoolean("verboseCalls", preferences.VerboseCalls);
        writer.WriteBoolean("verboseApprovals", preferences.VerboseApprovals);
        writer.WriteBoolean("verboseReview", preferences.VerboseReview);
        writer.WriteBoolean("verboseThinking", preferences.VerboseThinking);
        writer.WriteBoolean("statusLineEnabled", preferences.StatusLineEnabled);
        Strings(writer, "statusLineFields", preferences.StatusLineFields);
        writer.WriteStartArray("statusLineRules");
        foreach (var rule in preferences.StatusLineRules)
        {
            writer.WriteStartObject();
            writer.WriteString("field", rule.Field);
            writer.WriteBoolean("keep", rule.Keep);
            if (rule.HideBelow is { } hideBelow)
            {
                writer.WriteNumber("hideBelow", hideBelow);
            }
            else
            {
                writer.WriteNull("hideBelow");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        Strings(writer, "statusLineDrop", preferences.StatusLineDrop);
        writer.WriteBoolean("statusLineTitleCase", preferences.StatusLineTitleCase);
        writer.WriteString("theme", preferences.Theme);
        writer.WriteBoolean("approvalColors", preferences.ApprovalColors);
        writer.WriteEndObject();
    }

    public static void StatusFields(Utf8JsonWriter writer, string name, IReadOnlyList<StatusFieldValue> fields)
    {
        writer.WriteStartArray(name);
        foreach (var field in fields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            writer.WriteString("text", field.Text);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    /// <summary>The full completion tree, for a client that wants nested completion.</summary>
    public static void Commands(Utf8JsonWriter writer, string name, IReadOnlyList<SlashCompletion> commands)
    {
        writer.WriteStartArray(name);
        foreach (var command in commands)
        {
            writer.WriteStartObject();
            writer.WriteString("name", command.Name);
            writer.WriteString("help", command.Help);
            Strings(writer, "keys", command.Keys);
            writer.WriteBoolean("argumentsOptional", command.ArgumentsOptional);
            if (command.ArgumentOptions.Count > 0)
            {
                Commands(writer, "arguments", command.ArgumentOptions);
            }

            if (command.TrailingArgumentOptions.Count > 0)
            {
                Commands(writer, "argumentsAfterValue", command.TrailingArgumentOptions);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static void Todos(Utf8JsonWriter writer, string name, IEnumerable<TodoItem> todos)
    {
        writer.WriteStartArray(name);
        foreach (var todo in todos)
        {
            writer.WriteStartObject();
            writer.WriteString("id", todo.Id);
            writer.WriteString("content", todo.Content);
            writer.WriteString("status", TodoList.StatusName(todo.Status));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static void Document(Utf8JsonWriter writer, string name, TranscriptDocument document)
    {
        writer.WriteStartObject(name);
        writer.WriteString("title", document.Title);
        writer.WriteStartArray("blocks");
        foreach (var block in document.Blocks)
        {
            writer.WriteStartObject();
            writer.WriteString("type", block.Kind);
            writer.WriteStartArray("columns");
            foreach (var column in block.Columns)
            {
                writer.WriteStringValue(column);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("rows");
            foreach (var row in block.Rows)
            {
                writer.WriteStartArray();
                foreach (var cell in row)
                {
                    writer.WriteStringValue(cell);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("lines");
            foreach (var line in block.Lines)
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
            writer.WriteNumber("omitted", block.Omitted);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static void Items(Utf8JsonWriter writer, string name, IEnumerable<ChatItem> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items)
        {
            writer.WriteStartObject();
            switch (item)
            {
                case ChatMessage message:
                    writer.WriteString("kind", "message");
                    writer.WriteString("role", message.Role.Value);
                    Text(writer, "text", message.Text);
                    break;
                case ChatReasoningItem reasoning:
                    writer.WriteString("kind", "reasoning");
                    writer.WriteStartArray("segments");
                    foreach (var segment in reasoning.Content.TextSegments)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("kind", segment.Kind.Value);
                        writer.WriteString("text", segment.Text);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteBoolean("hasState", reasoning.Content.State is not null);
                    break;
                case ToolCall call:
                    writer.WriteString("kind", "tool_call");
                    Call(writer, call);
                    break;
                case ToolResult result:
                    writer.WriteString("kind", "tool_result");
                    writer.WriteString("callId", result.CallId);
                    writer.WriteString("status", result.Status.Value);
                    Text(writer, "text", result.Text);
                    break;
                default:
                    writer.WriteString("kind", "unknown");
                    break;
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static void Call(Utf8JsonWriter writer, ToolCall call)
    {
        writer.WriteString("callId", call.CallId);
        writer.WriteString("name", call.Name);
        writer.WriteString("arguments", call.Arguments);
        writer.WriteString("inputKind", call.InputKind.Value);
    }

    /// <summary>Text with trusted markers shown plain, and the image numbers it names.</summary>
    public static void Text(Utf8JsonWriter writer, string name, string text)
    {
        writer.WriteString(name, ImageMarkerText.Display(text));
        var images = ImageMarkerText.Matches(text)
            .Select(static match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
        if (images.Length == 0)
        {
            return;
        }

        writer.WriteStartArray("images");
        foreach (var number in images)
        {
            writer.WriteNumberValue(number);
        }

        writer.WriteEndArray();
    }

    public static void Side(Utf8JsonWriter writer, string name, SideQuestionSnapshot side)
    {
        writer.WriteStartObject(name);
        writer.WriteBoolean("announce", side.Announce);
        writer.WriteBoolean("running", side.Running);
        writer.WriteBoolean("thinking", side.Thinking);
        writer.WriteString("pendingQuestion", side.PendingQuestion);
        writer.WriteString("liveAnswer", side.LiveAnswer);
        Optional(writer, "failure", side.Failure);
        writer.WriteStartArray("exchanges");
        foreach (var exchange in side.Exchanges)
        {
            writer.WriteStartObject();
            writer.WriteString("question", exchange.Question);
            writer.WriteString("answer", exchange.Answer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>A child without its system prompt, model context, or image bytes. <paramref name="full"/> adds the archive.</summary>
    public static void Child(Utf8JsonWriter writer, ChildSessionDocument child, bool full)
    {
        writer.WriteStartObject();
        writer.WriteString("id", child.Id);
        writer.WriteString("parentCallId", child.ParentCallId);
        writer.WriteString("agent", child.Agent);
        writer.WriteString("label", child.Label);
        writer.WriteString("status", child.Status);
        Optional(writer, "stopReason", child.StopReason);
        writer.WriteStartObject("model");
        Optional(writer, "provider", child.Model.Provider);
        Optional(writer, "model", child.Model.Model);
        Optional(writer, "thinking", child.Model.Thinking);
        if (child.Model.Context is { } context)
        {
            writer.WriteNumber("contextWindow", context);
        }

        Optional(writer, "serviceTier", child.Model.Tier);
        Optional(writer, "speed", child.Model.Speed);
        writer.WriteEndObject();
        Strings(writer, "tools", child.Tools);
        Todos(writer, "todos", ChildSessionFold.Todos(child));
        Usage(writer, "usage", ChildSessionFold.Usage(child.Usage));
        Usage(writer, "cumulativeUsage", ChildSessionFold.Usage(child.CumulativeUsage));
        writer.WriteNumber("modelCalls", child.ModelCalls);
        writer.WriteNumber("toolCalls", child.ToolCalls);
        Strings(writer, "notes", child.Notes);
        if (full)
        {
            Items(writer, "archive", TranscriptCodec.Read(child.Archive));
        }

        writer.WriteEndObject();
    }

    public static void Status(Utf8JsonWriter writer, string name, SessionStatus status)
    {
        writer.WriteStartObject(name);
        writer.WriteString("sessionId", status.SessionId);
        writer.WriteString("startedUtc", status.StartedUtc);
        writer.WriteString("workspace", status.WorkspaceRoot);
        writer.WriteString("mode", status.Mode ?? (status.PlanMode ? "plan" : "work"));
        writer.WriteString("approval", status.Approval.Value);
        writer.WriteString("thinking", status.Thinking);
        writer.WriteString("promptSet", status.PromptSet);
        writer.WriteString("provider", status.Provider);
        writer.WriteString("model", status.Model);
        writer.WriteNumber("contextWindow", status.ContextWindow);
        writer.WriteString("serviceTier", status.ServiceTier);
        writer.WriteString("speed", status.Speed);
        Usage(writer, "usage", status.Usage);
        Usage(writer, "cumulativeUsage", status.CumulativeUsage);
        writer.WriteNumber("userTurns", status.UserTurns);
        writer.WriteNumber("modelCalls", status.ModelCalls);
        writer.WriteNumber("toolCalls", status.ToolCalls);
        writer.WriteNumber("queuedMessages", status.QueuedMessages);
        writer.WriteNumber("todos", status.Todos);
        writer.WriteNumber("planTools", status.PlanTools);
        writer.WriteNumber("workTools", status.WorkTools);
        writer.WriteNumber("externalTools", status.ExternalTools);
        writer.WriteNumber("plugins", status.Plugins);
        writer.WriteNumber("mcpServers", status.McpServers);
        writer.WriteNumber("mcpTools", status.McpTools);
        writer.WriteBoolean("skillsEnabled", status.SkillsEnabled);
        writer.WriteBoolean("externalToolsEnabled", status.ExternalToolsEnabled);
        writer.WriteBoolean("pluginsEnabled", status.PluginsEnabled);
        writer.WriteBoolean("mcpEnabled", status.McpEnabled);
        writer.WriteBoolean("globalRulesEnabled", status.GlobalRulesEnabled);
        writer.WriteBoolean("projectRulesEnabled", status.ProjectRulesEnabled);
        writer.WriteBoolean("estimatedTokensEnabled", status.EstimatedTokensEnabled);
        writer.WriteBoolean("customStatusLineEnabled", status.CustomStatusLineEnabled);
        Optional(writer, "approvalModel", status.ApprovalModel);
        Optional(writer, "approvalThinking", status.ApprovalThinking);
        Optional(writer, "extendedApprovalModel", status.ExtendedApprovalModel);
        Optional(writer, "extendedApprovalThinking", status.ExtendedApprovalThinking);
        Optional(writer, "extendedApprovalTrigger", status.ExtendedApprovalTrigger);
        if (status.ExtendedApprovalPrimaryReview is { } primary)
        {
            writer.WriteBoolean("extendedApprovalPrimaryReview", primary);
        }

        Optional(writer, "compactionModel", status.CompactionModel);
        Optional(writer, "microCompaction", status.MicroCompaction);
        Optional(writer, "retry", status.Retry);
        Optional(writer, "approvalRetry", status.ApprovalRetry);
        Pending(writer, "pending", status.Pending);
        writer.WriteEndObject();
    }

    public static void Stats(Utf8JsonWriter writer, string name, SessionStatsReport report, bool allWorkspaces)
    {
        writer.WriteStartObject(name);
        writer.WriteBoolean("allWorkspaces", allWorkspaces);
        writer.WriteNumber("sessions", report.Sessions);
        writer.WriteNumber("userTurns", report.UserTurns);
        writer.WriteNumber("modelCalls", report.ModelCalls);
        writer.WriteNumber("toolCalls", report.ToolCalls);
        writer.WriteNumber("inputTokens", report.InputTokens);
        writer.WriteNumber("outputTokens", report.OutputTokens);
        writer.WriteNumber("reasoningTokens", report.ReasoningTokens);
        writer.WriteNumber("totalTokens", report.TotalTokens);
        writer.WriteNumber("averageTokensPerSession", report.AverageTokensPerSession);
        writer.WriteNumber("medianTokensPerSession", report.MedianTokensPerSession);
        writer.WriteNumber("windowDays", report.WindowDays);
        Date(writer, "earliest", report.Earliest);
        Date(writer, "latest", report.Latest);
        writer.WriteStartArray("topTools");
        foreach (var tool in report.TopTools)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteNumber("count", tool.Count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static void ToolCatalog(Utf8JsonWriter writer, string name, ToolCatalogReport report)
    {
        writer.WriteStartObject(name);
        Definitions(writer, "plan", report.Plan);
        Definitions(writer, "work", report.Work);
        writer.WriteStartArray("external");
        foreach (var tool in report.External)
        {
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("set", tool.SetName);
            writer.WriteString("source", tool.Source.Value);
            writer.WriteBoolean("plan", tool.Catalogs.Plan);
            writer.WriteBoolean("work", tool.Catalogs.Work);
            writer.WriteString("declaredApproval", tool.DeclaredApproval.Value);
            writer.WriteString("effectiveApproval", tool.EffectiveApproval.Value);
            writer.WriteBoolean("hasApprovalClassifier", tool.HasApprovalClassifier);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        Strings(writer, "externalNotes", report.ExternalNotes);
        writer.WriteBoolean("externalToolsEnabled", report.ExternalToolsEnabled);
        writer.WriteStartObject("externalApproval");
        writer.WriteString("home", report.ExternalApproval.Home.Value);
        writer.WriteString("project", report.ExternalApproval.Project.Value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    public static string ActivityName(SessionActivity activity) => activity switch
    {
        SessionActivity.Idle => "idle",
        SessionActivity.LoadingTools => "loading_tools",
        SessionActivity.LoadingPlugins => "loading_plugins",
        SessionActivity.Compacting => "compacting",
        SessionActivity.NamingTopic => "naming_topic",
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

    /// <summary>The fields of the server's <c>GET /snapshot</c>, without its interactions.</summary>
    public static void Snapshot(Utf8JsonWriter writer, SessionSnapshot snapshot)
    {
        writer.WriteStartObject();
        Chrome(writer, "chrome", snapshot.Chrome);
        Preferences(writer, "preferences", snapshot.Preferences);
        Commands(writer, "commands", snapshot.Commands);
        Strings(writer, "promptHistory", snapshot.PromptHistory);
        Items(writer, "conversation", snapshot.Conversation);
        if (snapshot.Live is { } live)
        {
            writer.WriteStartObject("live");
            writer.WriteString("text", ImageMarkerText.Display(live.Text));
            writer.WriteString("reasoning", live.Reasoning);
            writer.WriteStartArray("toolCalls");
            foreach (var call in live.ToolCalls)
            {
                writer.WriteStartObject();
                Call(writer, call);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("retries", live.Retries);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("live");
        }

        writer.WriteBoolean("turnActive", snapshot.TurnActive);
        writer.WriteString("activity", ActivityName(snapshot.Activity));
        Strings(writer, "queue", snapshot.Queue.Select(ImageMarkerText.Display));
        Todos(writer, "todos", snapshot.Todos);
        StatusFields(writer, "statusFields", snapshot.StatusFields);
        Usage(writer, "usage", snapshot.Usage);
        Usage(writer, "cumulativeUsage", snapshot.CumulativeUsage);
        writer.WriteNumber("contextWindow", snapshot.ContextWindow);
        if (snapshot.EstimatedContextTokens is { } estimate)
        {
            writer.WriteNumber("estimatedContextTokens", estimate);
        }
        else
        {
            writer.WriteNull("estimatedContextTokens");
        }

        Side(writer, "side", snapshot.Side);
        writer.WriteStartArray("children");
        foreach (var child in snapshot.Children)
        {
            Child(writer, child, full: false);
        }

        writer.WriteEndArray();
        if (snapshot.LastTurn is { } last)
        {
            writer.WriteStartObject("lastTurn");
            writer.WriteString("reason", last.Result.StopReason.Value);
            writer.WriteNumber("modelCalls", last.Result.ModelCallCount);
            writer.WriteNumber("toolCalls", last.Result.ToolCallCount);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("lastTurn");
        }

        writer.WriteStartArray("approvals");
        foreach (var passed in snapshot.Approvals)
        {
            writer.WriteStartObject();
            writer.WriteStartObject("call");
            Call(writer, passed.Call);
            writer.WriteEndObject();
            writer.WriteString("reason", passed.Reason.Value);
            writer.WriteString("risk", passed.Classification.Risk.Value);
            writer.WriteString("authority", passed.Classification.Authority.Value);
            writer.WriteString("summary", passed.Classification.Summary);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static void Optional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    public static void Date(Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value is { } date)
        {
            writer.WriteString(name, date);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void Definitions(Utf8JsonWriter writer, string name, IEnumerable<ToolDefinition> definitions)
    {
        writer.WriteStartArray(name);
        foreach (var definition in definitions)
        {
            writer.WriteStartObject();
            writer.WriteString("name", definition.Name);
            Optional(writer, "description", definition.Description);
            writer.WriteString("inputKind", definition.Input.Kind.Value);
            if (definition.Input is JsonToolInput json)
            {
                writer.WritePropertyName("schema");
                json.Schema.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
