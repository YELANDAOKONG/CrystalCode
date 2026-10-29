using System.Text.Json;

using CrystalCode.Bridge.Prompts;
using CrystalCode.Bridge.Transport;
using CrystalCode.Bridge.Wire;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// The <c>_crystal/*</c> extension methods: every engine operation ACP has no
/// method for. Each is a request on the active session (<c>sessionId</c>)
/// with an explicit JSON result, and reaches the engine the way the matching
/// <c>crystal server</c> route does. <c>_crystal/session/fork</c> is answered
/// by <see cref="BridgeAgent"/> with <c>session/fork</c>.
/// </summary>
internal static class Extensions
{
    /// <summary>Every extension method this build answers, for <c>initialize</c>.</summary>
    public static IReadOnlyList<string> Methods { get; } =
    [
        "_crystal/snapshot",
        "_crystal/session/fork",
        "_crystal/recall",
        "_crystal/queue/release",
        "_crystal/mode/cycle",
        "_crystal/mode/togglePlan",
        "_crystal/verbose",
        "_crystal/draft",
        "_crystal/image/attach",
        "_crystal/image/get",
        "_crystal/quickPrompts/list",
        "_crystal/quickPrompts/send",
        "_crystal/side/ask",
        "_crystal/side/cancel",
        "_crystal/side/clear",
        "_crystal/children/list",
        "_crystal/children/get",
        "_crystal/export",
        "_crystal/compact",
        "_crystal/status",
        "_crystal/stats",
        "_crystal/tools",
        "_crystal/models",
        "_crystal/agents",
        "_crystal/skills",
        "_crystal/mcp",
        "_crystal/plugins",
        "_crystal/tasks",
        "_crystal/tasks/logs"
    ];

    /// <summary>Every <c>_crystal/*</c> notification Bridge sends, for <c>initialize</c>.</summary>
    public static IReadOnlyList<string> Notifications { get; } =
    [
        "_crystal/chrome",
        "_crystal/preferences",
        "_crystal/statusFields",
        "_crystal/promptHistory",
        "_crystal/imagesInvalidated",
        "_crystal/activity",
        "_crystal/recalled",
        "_crystal/queue",
        "_crystal/side",
        "_crystal/childView",
        "_crystal/report"
    ];

    public static async Task<JsonRpcReply> HandleAsync(
        string method,
        BridgeSession bridge,
        JsonElement parameters,
        ClientFeatures features,
        string origin)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        var session = bridge.Session!;
        var runner = bridge.Runner!;
        switch (method)
        {
            case "_crystal/snapshot":
                var snapshot = bridge.Snapshot();
                return new JsonRpcReply(writer => CrystalWire.Snapshot(writer, snapshot));
            case "_crystal/recall":
                // Ctrl+C semantics: a direct call, not queued behind the running turn.
                var recalled = session.TryRecall();
                return Result(writer => writer.WriteBoolean("recalled", recalled));
            case "_crystal/queue/release":
                await SubmitAsync(bridge, string.Empty, origin);
                return JsonRpcReply.Empty;
            case "_crystal/mode/cycle":
                await runner.InvokeAsync(static value => value.CycleMode(), origin);
                return Mode(session);
            case "_crystal/mode/togglePlan":
                await runner.InvokeAsync(static value => value.TogglePlan(), origin);
                return Mode(session);
            case "_crystal/verbose":
                var target = JsonParams.RequiredString(parameters, "target");
                if (!Enum.TryParse<VerboseTarget>(target, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(parsed)
                    || !parameters.TryGetProperty("enabled", out var enabled)
                    || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw JsonRpcException.InvalidParams(
                        "target must be tools, commands, results, calls, approvals, review, or thinking, and enabled must be true or false.");
                }

                var on = enabled.GetBoolean();
                await runner.InvokeAsync(value =>
                {
                    value.SetVerbose(parsed, on);
                    return true;
                }, origin);
                return Result(writer => writer.WriteBoolean("enabled", on));
            case "_crystal/draft":
                var draft = JsonParams.RequiredString(parameters, "text");
                await runner.InvokeAsync(value =>
                {
                    value.NotifyDraftChanged(draft);
                    return true;
                }, origin);
                return JsonRpcReply.Empty;
            case "_crystal/image/attach":
                return await AttachAsync(bridge, parameters, origin);
            case "_crystal/image/get":
                return Image(session, parameters);
            case "_crystal/quickPrompts/list":
                var catalog = await bridge.QuickPrompts.LoadAsync(CancellationToken.None);
                return Result(writer => QuickPrompts(writer, catalog));
            case "_crystal/quickPrompts/send":
                return await SendQuickPromptAsync(bridge, parameters, origin);
            case "_crystal/side/ask":
                var question = JsonParams.RequiredString(parameters, "question");
                if (string.IsNullOrWhiteSpace(question))
                {
                    throw JsonRpcException.InvalidParams("question must not be empty.");
                }

                await SubmitAsync(bridge, "/btw " + PromptContent.OneLine(question), origin);
                return JsonRpcReply.Empty;
            case "_crystal/side/cancel":
                var cancelled = session.TryCancelSideQuestion();
                return Result(writer => writer.WriteBoolean("cancelled", cancelled));
            case "_crystal/side/clear":
                await runner.InvokeAsync(static value =>
                {
                    value.ClearSideQuestions();
                    return true;
                }, origin);
                return JsonRpcReply.Empty;
            case "_crystal/children/list":
                var children = bridge.Snapshot().Children;
                return Result(writer =>
                {
                    writer.WriteStartArray("children");
                    foreach (var child in children)
                    {
                        CrystalWire.Child(writer, child, full: false);
                    }

                    writer.WriteEndArray();
                });
            case "_crystal/children/get":
                var id = JsonParams.RequiredString(parameters, "childSessionId");
                var found = bridge.Snapshot().Children.FirstOrDefault(child => string.Equals(child.Id, id, StringComparison.Ordinal))
                    ?? throw JsonRpcException.NotFound("No matching child session.");
                return Result(writer =>
                {
                    writer.WritePropertyName("child");
                    CrystalWire.Child(writer, found, full: true);
                });
            case "_crystal/export":
                return await ExportAsync(runner, parameters, origin);
            case "_crystal/compact":
                var full = parameters.TryGetProperty("full", out var fullValue) && fullValue.ValueKind == JsonValueKind.True;
                await SubmitAsync(bridge, full ? "/compact full" : "/compact", origin);
                return JsonRpcReply.Empty;
            case "_crystal/status":
                return await ReportAsync(bridge, parameters.TryGetProperty("full", out var detail) && detail.ValueKind == JsonValueKind.True
                    ? "/status full"
                    : "/status", "status", origin);
            case "_crystal/stats":
                return await ReportAsync(bridge, JsonParams.OptionalString(parameters, "scope") is { Length: > 0 } scope
                    ? "/stats " + PromptContent.OneLine(scope)
                    : "/stats", "stats", origin);
            case "_crystal/tools":
                return await ReportAsync(bridge, "/tools", "tools", origin);
            case "_crystal/models":
                var (models, pending) = await QueryAsync(runner, static value => (value.ListModels(), value.PendingSettings), origin);
                return Result(writer => CatalogWire.Models(writer, models, pending));
            case "_crystal/agents":
                var agents = await QueryAsync(runner, static value => value.ListAgents(), origin);
                return Result(writer => CatalogWire.Agents(writer, agents));
            case "_crystal/skills":
                var skills = await QueryAsync(runner, static value => value.ListSkills(), origin);
                return Result(writer => CatalogWire.Skills(writer, skills));
            case "_crystal/mcp":
                var mcp = await QueryAsync(runner, static value => value.ListMcp(), origin);
                return Result(writer => CatalogWire.Mcp(writer, mcp));
            case "_crystal/plugins":
                var plugins = await QueryAsync(runner, static value => value.ListPlugins(), origin);
                return Result(writer => CatalogWire.Plugins(writer, plugins));
            case "_crystal/tasks":
                var tasks = await QueryAsync(runner, static value => value.ListBackgroundTasks(), origin);
                return Result(writer => CatalogWire.Tasks(writer, tasks));
            case "_crystal/tasks/logs":
                return await TaskLogAsync(runner, parameters, origin);
            default:
                throw new JsonRpcException(JsonRpcErrorCode.MethodNotFound, "Method not found: " + PromptContent.OneLine(method));
        }
    }

    /// <summary>Reads the session through the runner, so the read sees a consistent session.</summary>
    private static async Task<T> QueryAsync<T>(SessionRunner runner, Func<CodingSession, T> read, string origin)
    {
        try
        {
            return await runner.InvokeAsync(read, origin);
        }
        catch (InvalidOperationException)
        {
            throw JsonRpcException.NotFound("The session is not active.");
        }
    }

    private static async Task<JsonRpcReply> TaskLogAsync(SessionRunner runner, JsonElement parameters, string origin)
    {
        var taskId = JsonParams.RequiredString(parameters, "taskId");
        var offset = OptionalInt(parameters, "offset") ?? 0;
        var limit = OptionalInt(parameters, "limit") ?? WorkspaceLimits.MaximumBackgroundLogCharacters;
        var (exists, read, log) = await QueryAsync(runner, value =>
        {
            var known = value.ListBackgroundTasks().Any(task => string.Equals(task.TaskId, taskId, StringComparison.Ordinal));
            var ok = value.TryReadBackgroundTaskLog(taskId, offset, limit, out var text);
            return (known, ok, text);
        }, origin);
        if (!exists)
        {
            throw JsonRpcException.NotFound(log.Text);
        }

        if (!read)
        {
            throw JsonRpcException.InvalidParams(log.Text);
        }

        return Result(writer => CatalogWire.Log(writer, log));
    }

    private static int? OptionalInt(JsonElement parameters, string name)
    {
        if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw JsonRpcException.InvalidParams(name + " must be an integer.");
    }

    /// <summary>Submits text as the composer does and turns an engine refusal into <c>command_refused</c>.</summary>
    private static async Task SubmitAsync(BridgeSession bridge, string text, string origin)
    {
        try
        {
            await bridge.Runner!.SubmitAsync(text, origin);
        }
        catch (InvalidOperationException)
        {
            throw JsonRpcException.NotFound("The session is not active.");
        }

        if (bridge.TakeError(origin) is { } refused)
        {
            throw JsonRpcException.Refused("command_refused", refused);
        }
    }

    /// <summary>Runs a report command and answers with its structured values.</summary>
    private static async Task<JsonRpcReply> ReportAsync(BridgeSession bridge, string command, string kind, string origin)
    {
        using var capture = bridge.CaptureReport(kind);
        await SubmitAsync(bridge, command, origin);
        var values = capture.Values ?? throw JsonRpcException.Refused("command_refused", "The report was not produced.");
        return new JsonRpcReply(values);
    }

    private static JsonRpcReply Mode(CodingSession session)
    {
        var plan = session.PlanMode;
        var custom = session.CustomMode;
        var pending = session.PendingSettings;
        return Result(writer =>
        {
            writer.WriteString("mode", custom ?? (plan ? "plan" : "work"));
            writer.WriteBoolean("planMode", plan);
            CrystalWire.Pending(writer, "pending", pending);
        });
    }

    private static async Task<JsonRpcReply> AttachAsync(BridgeSession bridge, JsonElement parameters, string origin)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(JsonParams.RequiredString(parameters, "data"));
        }
        catch (FormatException)
        {
            throw JsonRpcException.InvalidParams("data is not valid base64.");
        }

        var (attached, image, error) = await bridge.Runner!.InvokeAsync(session =>
        {
            var ok = session.TryAttachImage(bytes, out var value, out var message);
            return (ok, value, message);
        }, origin);
        if (!attached || image is null)
        {
            throw JsonRpcException.InvalidParams(error);
        }

        bridge.Attached(image.Number);
        return Result(writer =>
        {
            writer.WriteNumber("number", image.Number);
            writer.WriteString("mimeType", image.MimeType);
            writer.WriteString("marker", image.Marker);
            writer.WriteString("trustedMarker", image.TrustedMarker);
        });
    }

    private static JsonRpcReply Image(CodingSession session, JsonElement parameters)
    {
        if (!parameters.TryGetProperty("number", out var number)
            || !number.TryGetInt32(out var value)
            || value <= 0)
        {
            throw JsonRpcException.InvalidParams("number must be a positive integer.");
        }

        if (!session.TryGetImage(value, out var image) || image is not { Data: { } data })
        {
            throw JsonRpcException.NotFound("The session holds no image with that number.");
        }

        var bytes = data.ToArray();
        return Result(writer =>
        {
            writer.WriteNumber("number", image.Number);
            writer.WriteString("mimeType", image.MimeType);
            writer.WriteBase64String("data", bytes);
        });
    }

    private static async Task<JsonRpcReply> SendQuickPromptAsync(BridgeSession bridge, JsonElement parameters, string origin)
    {
        var name = JsonParams.RequiredString(parameters, "name");
        var catalog = await bridge.QuickPrompts.LoadAsync(CancellationToken.None);
        var prompt = catalog.Entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))
            ?? throw JsonRpcException.NotFound("Quick prompt not found  " + PromptContent.OneLine(name));
        if (prompt.Error is not null)
        {
            throw JsonRpcException.InvalidParams(prompt.Error);
        }

        SessionInputResult result;
        try
        {
            result = await bridge.Runner!.SubmitQuickPromptAsync(prompt.Text, origin);
        }
        catch (ArgumentException exception)
        {
            throw JsonRpcException.InvalidParams(exception.Message);
        }

        return Result(writer => writer.WriteBoolean("turnActive", result.TurnActive));
    }

    private static async Task<JsonRpcReply> ExportAsync(SessionRunner runner, JsonElement parameters, string origin)
    {
        var format = JsonParams.OptionalString(parameters, "format") ?? "markdown";
        var system = parameters.TryGetProperty("system", out var value) && value.ValueKind == JsonValueKind.True;
        var (ok, content, error) = await runner.InvokeAsync(session =>
        {
            var exported = session.TryExport(format, system, out var rendered, out var message);
            return (exported, rendered, message);
        }, origin);
        if (!ok || content is null)
        {
            throw JsonRpcException.InvalidParams(error);
        }

        return Result(writer =>
        {
            writer.WriteString("fileName", content.FileName);
            writer.WriteString("mediaType", content.MediaType);
            writer.WriteString("text", content.Text);
        });
    }

    private static void QuickPrompts(Utf8JsonWriter writer, QuickPromptCatalog catalog)
    {
        writer.WriteString("directory", catalog.Directory);
        CrystalWire.Optional(writer, "error", catalog.Error);
        writer.WriteStartArray("prompts");
        foreach (var entry in catalog.Entries)
        {
            writer.WriteStartObject();
            writer.WriteString("name", entry.Name);
            writer.WriteString("text", entry.Text);
            CrystalWire.Optional(writer, "error", entry.Error);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static JsonRpcReply Result(Action<Utf8JsonWriter> fields) =>
        new(writer =>
        {
            writer.WriteStartObject();
            fields(writer);
            writer.WriteEndObject();
        });
}
