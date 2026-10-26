using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Server.Wire;

/// <summary>
/// Writes engine values as wire JSON with camelCase fields. Every value goes
/// through an explicit mapping: no engine record is serialized by reflection,
/// so settings, provider definitions, opaque reasoning state, and image
/// bytes can never reach a client by accident.
/// </summary>
public static class WireWriter
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
        writer.WriteEndObject();
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
        writer.WriteEndObject();
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

    public static void Commands(Utf8JsonWriter writer, string name, IReadOnlyList<SlashCompletion> commands)
    {
        writer.WriteStartArray(name);
        foreach (var command in commands)
        {
            Command(writer, command);
        }

        writer.WriteEndArray();
    }

    private static void Command(Utf8JsonWriter writer, SlashCompletion command)
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

    public static void Items(Utf8JsonWriter writer, string name, IEnumerable<ChatItem> items)
    {
        writer.WriteStartArray(name);
        foreach (var item in items)
        {
            Item(writer, item);
        }

        writer.WriteEndArray();
    }

    public static void Item(Utf8JsonWriter writer, ChatItem item)
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
                Result(writer, result);
                break;
            default:
                writer.WriteString("kind", "unknown");
                break;
        }

        writer.WriteEndObject();
    }

    public static void Call(Utf8JsonWriter writer, ToolCall call)
    {
        writer.WriteString("callId", call.CallId);
        writer.WriteString("name", call.Name);
        writer.WriteString("arguments", call.Arguments);
        writer.WriteString("inputKind", call.InputKind.Value);
    }

    public static void Calls(Utf8JsonWriter writer, string name, IEnumerable<ToolCall> calls)
    {
        writer.WriteStartArray(name);
        foreach (var call in calls)
        {
            writer.WriteStartObject();
            Call(writer, call);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static void Result(Utf8JsonWriter writer, ToolResult result)
    {
        writer.WriteString("callId", result.CallId);
        writer.WriteString("status", result.Status.Value);
        Text(writer, "text", result.Text);
    }

    /// <summary>
    /// Text with trusted image markers shown as plain <c>[Image #N]</c>, plus
    /// the image numbers a client may fetch from <c>/images/{number}</c>.
    /// </summary>
    public static void Text(Utf8JsonWriter writer, string name, string text)
    {
        writer.WriteString(name, ImageMarkerText.Display(text));
        var images = ImageMarkerText.Matches(text)
            .Select(static match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
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

    public static void Classification(Utf8JsonWriter writer, string name, ToolClassification classification)
    {
        writer.WriteStartObject(name);
        writer.WriteString("risk", classification.Risk.Value);
        writer.WriteString("authority", classification.Authority.Value);
        writer.WriteString("summary", classification.Summary);
        writer.WriteBoolean("requirePrompt", classification.RequirePrompt);
        writer.WriteBoolean("allowsReusableGrant", classification.AllowsReusableGrant);
        writer.WriteEndObject();
    }

    public static void Verdict(Utf8JsonWriter writer, string name, ApprovalReviewVerdict? verdict)
    {
        if (verdict is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteString("outcome", verdict.Outcome);
        writer.WriteString("riskLevel", verdict.RiskLevel.Value);
        writer.WriteString("userAuthorization", verdict.UserAuthorization.Value);
        writer.WriteString("rationale", verdict.Rationale);
        writer.WriteEndObject();
    }

    public static void Side(Utf8JsonWriter writer, string name, SideQuestionSnapshot side)
    {
        writer.WriteStartObject(name);
        writer.WriteBoolean("announce", side.Announce);
        writer.WriteBoolean("running", side.Running);
        writer.WriteBoolean("thinking", side.Thinking);
        writer.WriteString("pendingQuestion", side.PendingQuestion);
        writer.WriteString("liveAnswer", side.LiveAnswer);
        if (side.Failure is null)
        {
            writer.WriteNull("failure");
        }
        else
        {
            writer.WriteString("failure", side.Failure);
        }

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

    /// <summary>
    /// A child summary without its system prompt, model context, or image
    /// bytes. <paramref name="full"/> adds the archive.
    /// </summary>
    public static void Child(Utf8JsonWriter writer, ChildSessionDocument child, bool full)
    {
        writer.WriteStartObject();
        writer.WriteString("id", child.Id);
        writer.WriteString("parentCallId", child.ParentCallId);
        writer.WriteString("agent", child.Agent);
        writer.WriteString("label", child.Label);
        writer.WriteString("status", child.Status);
        if (child.StopReason is null)
        {
            writer.WriteNull("stopReason");
        }
        else
        {
            writer.WriteString("stopReason", child.StopReason);
        }

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

    public static void ToolCatalog(Utf8JsonWriter writer, string name, ToolCatalogReport report)
    {
        writer.WriteStartObject(name);
        Definitions(writer, "plan", report.Plan);
        Definitions(writer, "work", report.Work);
        writer.WriteStartArray("external");
        foreach (var tool in report.External)
        {
            ExternalTool(writer, tool);
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

    private static void ExternalTool(Utf8JsonWriter writer, ExternalToolInfo tool)
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
        writer.WriteEndObject();
    }

    public static void Stats(Utf8JsonWriter writer, string name, SessionStatsReport report)
    {
        writer.WriteStartObject(name);
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

    public static void SessionSummary(Utf8JsonWriter writer, SessionSummary summary)
    {
        writer.WriteStartObject();
        writer.WriteString("id", summary.Id);
        writer.WriteString("workspace", summary.Workspace);
        writer.WriteString("mode", summary.Mode ?? (summary.PlanMode ? "plan" : "work"));
        Date(writer, "createdUtc", summary.CreatedUtc);
        Date(writer, "updatedUtc", summary.UpdatedUtc);
        writer.WriteNumber("userTurns", summary.UserTurns);
        writer.WriteString("preview", summary.Preview);
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
}
