using System.Text.Json;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// The structured catalog results of <c>_crystal/models</c>, <c>agents</c>,
/// <c>skills</c>, <c>mcp</c>, <c>plugins</c>, <c>tasks</c>, and
/// <c>tasks/logs</c>, with the field names of the matching
/// <c>crystal server</c> routes. Every field is written explicitly.
/// </summary>
internal static class CatalogWire
{
    public static void Models(Utf8JsonWriter writer, IReadOnlyList<ModelCatalogEntry> models, SessionPendingSettings pending)
    {
        writer.WriteStartArray("models");
        foreach (var model in models)
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
            CrystalWire.Strings(writer, "thinkingEfforts", model.ThinkingEfforts);
            CrystalWire.Strings(writer, "serviceTiers", model.ServiceTiers);
            CrystalWire.Strings(writer, "speeds", model.Speeds);
            writer.WriteBoolean("imageInput", model.ImageInput);
            Number(writer, "maxTokens", model.MaxTokens);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        CrystalWire.Pending(writer, "pending", pending);
    }

    public static void Agents(Utf8JsonWriter writer, AgentCatalogReport report)
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
            CrystalWire.Optional(writer, "provider", agent.Provider);
            CrystalWire.Optional(writer, "model", agent.Model);
            CrystalWire.Optional(writer, "thinking", agent.Thinking);
            Number(writer, "context", agent.Context);
            CrystalWire.Optional(writer, "tier", agent.Tier);
            CrystalWire.Optional(writer, "speed", agent.Speed);
            writer.WriteEndObject();
            if (agent.Tools is { } tools)
            {
                CrystalWire.Strings(writer, "tools", tools);
            }
            else
            {
                writer.WriteNull("tools");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        CrystalWire.Strings(writer, "notes", report.Notes);
    }

    public static void Skills(Utf8JsonWriter writer, SkillCatalogReport report)
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
    }

    public static void Mcp(Utf8JsonWriter writer, McpCatalogReport report)
    {
        writer.WriteBoolean("enabled", report.Enabled);
        writer.WriteStartArray("servers");
        foreach (var server in report.Servers)
        {
            writer.WriteStartObject();
            writer.WriteString("name", server.Name);
            writer.WriteString("source", server.Source);
            writer.WriteString("transport", server.Transport);
            writer.WriteString("status", server.Status);
            CrystalWire.Strings(writer, "tools", server.Tools);
            writer.WriteString("error", server.Error);
            writer.WriteBoolean("enabled", server.Enabled);
            writer.WriteBoolean("effective", server.Effective);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        CrystalWire.Strings(writer, "notes", report.Notes);
    }

    public static void Plugins(Utf8JsonWriter writer, PluginCatalogReport report)
    {
        writer.WriteBoolean("enabled", report.Enabled);
        writer.WriteStartArray("plugins");
        foreach (var plugin in report.Plugins)
        {
            writer.WriteStartObject();
            writer.WriteString("directory", plugin.Directory);
            writer.WriteString("source", plugin.Source);
            CrystalWire.Optional(writer, "name", plugin.Name);
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
        CrystalWire.Strings(writer, "notes", report.Notes);
    }

    public static void Tasks(Utf8JsonWriter writer, IReadOnlyList<BackgroundTaskSnapshot> tasks)
    {
        writer.WriteStartArray("tasks");
        foreach (var task in tasks)
        {
            writer.WriteStartObject();
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
            CrystalWire.Date(writer, "finishedUtc", task.FinishedUtc);
            writer.WriteNumber("logStartOffset", task.LogStartOffset);
            writer.WriteNumber("logEndOffset", task.LogEndOffset);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    public static void Log(Utf8JsonWriter writer, BackgroundTaskLog log)
    {
        writer.WriteString("text", log.Text);
        writer.WriteNumber("nextOffset", log.NextOffset);
        writer.WriteBoolean("hasMore", log.HasMore);
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
}
