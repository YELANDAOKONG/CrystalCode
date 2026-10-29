using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;
using CrystalCode.Server.Security;
using CrystalCode.Server.Wire;

namespace CrystalCode.Server.Endpoints;

/// <summary>
/// Structured read-only views of what the session lists as text through
/// slash commands: saved sessions, models, options, Agents, skills, MCP
/// servers, plugins, and background tasks. Each reads through
/// <see cref="SessionRunner"/>, so it sees a consistent session.
/// </summary>
internal static class QueryEndpoints
{
    public static void Map(WebApplication app, ServerContext server)
    {
        app.MapGet("/sessions", async Task<IResult> (HttpContext context) =>
        {
            var all = string.Equals(context.Request.Query["all"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
            return await Query(context, server, session => session.ListSavedSessions(all), static (writer, sessions) =>
            {
                writer.WriteStartArray("sessions");
                foreach (var saved in sessions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", saved.Id);
                    writer.WriteString("workspace", saved.Workspace);
                    writer.WriteString("mode", saved.Mode);
                    WireWriter.Date(writer, "createdUtc", saved.CreatedUtc);
                    WireWriter.Date(writer, "updatedUtc", saved.UpdatedUtc);
                    writer.WriteNumber("userTurns", saved.UserTurns);
                    writer.WriteString("preview", saved.Preview);
                    writer.WriteBoolean("current", saved.Current);
                    writer.WriteBoolean("open", saved.Open);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });
        });

        app.MapGet("/models", async Task<IResult> (HttpContext context) =>
            await Query(context, server, static session => (Models: session.ListModels(), Pending: session.PendingSettings), static (writer, value) =>
            {
                writer.WriteStartArray("models");
                foreach (var model in value.Models)
                {
                    writer.WriteStartObject();
                    writer.WriteString("provider", model.Provider);
                    writer.WriteString("model", model.Model);
                    writer.WriteString("protocol", model.Protocol);
                    writer.WriteBoolean("current", model.Current);
                    writer.WriteBoolean("credentialReady", model.CredentialReady);
                    writer.WriteStartArray("contextWindows");
                    foreach (var window in model.ContextWindows)
                    {
                        writer.WriteNumberValue(window);
                    }

                    writer.WriteEndArray();
                    writer.WriteBoolean("thinking", model.Thinking);
                    writer.WriteBoolean("thinkingCanDisable", model.ThinkingCanDisable);
                    WireWriter.Strings(writer, "thinkingEfforts", model.ThinkingEfforts);
                    WireWriter.Strings(writer, "serviceTiers", model.ServiceTiers);
                    WireWriter.Strings(writer, "speeds", model.Speeds);
                    writer.WriteBoolean("imageInput", model.ImageInput);
                    Number(writer, "maxTokens", model.MaxTokens);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                WireWriter.Pending(writer, "pending", value.Pending);
            }));

        app.MapGet("/options", async Task<IResult> (HttpContext context) =>
            await Query(context, server, static session => (Options: session.ListOptions(), Pending: session.PendingSettings), static (writer, value) =>
            {
                WireWriter.Options(writer, "options", value.Options);
                WireWriter.Pending(writer, "pending", value.Pending);
            }));

        app.MapGet("/agents", async Task<IResult> (HttpContext context) =>
            await Query(context, server, static session => session.ListAgents(), static (writer, report) =>
            {
                writer.WriteBoolean("enabled", report.Enabled);
                writer.WriteNumber("concurrency", report.Concurrency);
                writer.WriteStartObject("limits");
                Number(writer, "modelCalls", report.MaximumModelCalls);
                Number(writer, "toolCalls", report.MaximumToolCalls);
                Number(writer, "durationSeconds", report.MaximumDurationSeconds);
                writer.WriteEndObject();
                writer.WriteStartArray("agents");
                foreach (var agent in report.Agents)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", agent.Name);
                    writer.WriteString("description", agent.Description);
                    writer.WriteBoolean("custom", agent.Custom);
                    writer.WriteStartObject("model");
                    WireWriter.Optional(writer, "provider", agent.Provider);
                    WireWriter.Optional(writer, "model", agent.Model);
                    WireWriter.Optional(writer, "thinking", agent.Thinking);
                    Number(writer, "context", agent.Context);
                    WireWriter.Optional(writer, "tier", agent.Tier);
                    WireWriter.Optional(writer, "speed", agent.Speed);
                    writer.WriteEndObject();
                    if (agent.Tools is { } tools)
                    {
                        WireWriter.Strings(writer, "tools", tools);
                    }
                    else
                    {
                        writer.WriteNull("tools");
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                WireWriter.Strings(writer, "notes", report.Notes);
            }));

        app.MapGet("/skills", async Task<IResult> (HttpContext context) =>
            await Query(context, server, static session => session.ListSkills(), static (writer, report) =>
            {
                writer.WriteBoolean("enabled", report.Enabled);
                writer.WriteStartArray("skills");
                foreach (var skill in report.Skills)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", skill.Name);
                    writer.WriteString("description", skill.Description);
                    writer.WriteString("source", skill.Source);
                    writer.WriteString("location", skill.Location);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }));

        app.MapGet("/mcp", async Task<IResult> (HttpContext context) =>
            await Query(context, server, static session => session.ListMcp(), static (writer, report) =>
            {
                writer.WriteBoolean("enabled", report.Enabled);
                writer.WriteStartArray("servers");
                foreach (var mcp in report.Servers)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", mcp.Name);
                    writer.WriteString("source", mcp.Source);
                    writer.WriteString("transport", mcp.Transport);
                    writer.WriteString("status", mcp.Status);
                    WireWriter.Strings(writer, "tools", mcp.Tools);
                    writer.WriteString("error", mcp.Error);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                WireWriter.Strings(writer, "notes", report.Notes);
            }));

        app.MapGet("/plugins", async Task<IResult> (HttpContext context) =>
            await Query(context, server, static session => session.ListPlugins(), static (writer, report) =>
            {
                writer.WriteBoolean("enabled", report.Enabled);
                writer.WriteStartArray("plugins");
                foreach (var plugin in report.Plugins)
                {
                    writer.WriteStartObject();
                    writer.WriteString("directory", plugin.Directory);
                    writer.WriteString("source", plugin.Source);
                    WireWriter.Optional(writer, "name", plugin.Name);
                    if (plugin.Enabled is bool enabled)
                    {
                        writer.WriteBoolean("enabled", enabled);
                    }
                    else
                    {
                        writer.WriteNull("enabled");
                    }

                    writer.WriteBoolean("effective", plugin.Effective);
                    writer.WriteBoolean("loaded", plugin.Loaded);
                    writer.WriteNumber("tools", plugin.Tools);
                    writer.WriteNumber("commands", plugin.Commands);
                    writer.WriteNumber("hooks", plugin.Hooks);
                    writer.WriteString("error", plugin.Error);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                WireWriter.Strings(writer, "notes", report.Notes);
            }));

        app.MapGet("/tasks", async Task<IResult> (HttpContext context) =>
            await Query(context, server, static session => session.ListBackgroundTasks(), static (writer, tasks) =>
            {
                writer.WriteStartArray("tasks");
                foreach (var task in tasks)
                {
                    writer.WriteStartObject();
                    Task(writer, task);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }));

        app.MapGet("/tasks/{id}/logs", async Task<IResult> (HttpContext context, string id) =>
        {
            if (!TryInt(context.Request.Query["offset"].ToString(), 0, out var offset)
                || !TryInt(context.Request.Query["limit"].ToString(), WorkspaceLimits.MaximumBackgroundLogCharacters, out var limit))
            {
                return Json.Problem(400, "offset and limit must be integers.");
            }

            var failure = Guard(context, server, out var runner);
            if (failure is not null)
            {
                return failure;
            }

            var (exists, read, log) = await runner!.InvokeAsync(session =>
            {
                var known = session.ListBackgroundTasks().Any(task => string.Equals(task.TaskId, id, StringComparison.Ordinal));
                var ok = session.TryReadBackgroundTaskLog(id, offset, limit, out var value);
                return (known, ok, value);
            }, AccessGuard.Client(context));
            if (!exists)
            {
                return Json.Problem(404, log.Text);
            }

            if (!read)
            {
                return Json.Problem(log.OffsetExpired ? 410 : 400, log.Text);
            }

            return Json.Ok(writer =>
            {
                writer.WriteString("text", log.Text);
                writer.WriteNumber("nextOffset", log.NextOffset);
                writer.WriteBoolean("hasMore", log.HasMore);
            });
        });
    }

    private static void Task(Utf8JsonWriter writer, BackgroundTaskSnapshot task)
    {
        writer.WriteString("id", task.TaskId);
        writer.WriteString("command", task.Command);
        writer.WriteString("workingDirectory", task.WorkingDirectory);
        writer.WriteString("status", task.Status switch
        {
            BackgroundTaskStatus.Starting => "starting",
            BackgroundTaskStatus.Running => "running",
            BackgroundTaskStatus.Exited => "exited",
            BackgroundTaskStatus.Failed => "failed",
            BackgroundTaskStatus.TimedOut => "timed_out",
            BackgroundTaskStatus.Stopped => "stopped",
            _ => throw new InvalidOperationException("Unknown background task status.")
        });
        Number(writer, "exitCode", task.ExitCode);
        writer.WriteString("startedUtc", task.StartedUtc);
        WireWriter.Date(writer, "finishedUtc", task.FinishedUtc);
        writer.WriteNumber("logStartOffset", task.LogStartOffset);
        writer.WriteNumber("logEndOffset", task.LogEndOffset);
    }

    private static async Task<IResult> Query<T>(
        HttpContext context,
        ServerContext server,
        Func<CodingSession, T> read,
        Action<Utf8JsonWriter, T> write)
    {
        var failure = Guard(context, server, out var runner);
        if (failure is not null)
        {
            return failure;
        }

        try
        {
            var value = await runner!.InvokeAsync(read, AccessGuard.Client(context));
            return Json.Ok(writer => write(writer, value));
        }
        catch (InvalidOperationException)
        {
            return Json.Problem(503, "The session is closing.");
        }
    }

    private static IResult? Guard(HttpContext context, ServerContext server, out SessionRunner? runner)
    {
        runner = server.Session.Runner;
        if (AccessGuard.Scope(context) < ServerScope.Observe)
        {
            return Json.Problem(403, "This request needs the observe scope.");
        }

        return runner is null ? Json.Problem(503, "The session is not ready.") : null;
    }

    private static void Number(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is int number)
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static bool TryInt(string text, int fallback, out int value)
    {
        if (text.Length == 0)
        {
            value = fallback;
            return true;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
