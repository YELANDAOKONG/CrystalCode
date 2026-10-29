using System.Text.Json;

using CrystalCode.Engine.Sessions;
using CrystalCode.Server.Interactions;

namespace CrystalCode.Server.Wire;

/// <summary>Writes the fields of <c>GET /snapshot</c>.</summary>
public static class SnapshotWriter
{
    public static void Write(
        Utf8JsonWriter writer,
        SessionSnapshot snapshot,
        string state,
        IReadOnlyList<Interaction> interactions,
        string client)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(snapshot);
        writer.WriteString("state", state);
        WireWriter.Chrome(writer, "chrome", snapshot.Chrome);
        WireWriter.Preferences(writer, "preferences", snapshot.Preferences);
        WireWriter.Commands(writer, "commands", snapshot.Commands);
        WireWriter.Strings(writer, "promptHistory", snapshot.PromptHistory);
        WireWriter.Items(writer, "conversation", snapshot.Conversation);
        if (snapshot.Live is { } live)
        {
            writer.WriteStartObject("live");
            writer.WriteString("text", ImageMarkerText.Display(live.Text));
            writer.WriteString("reasoning", live.Reasoning);
            WireWriter.Calls(writer, "toolCalls", live.ToolCalls);
            writer.WriteNumber("retries", live.Retries);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("live");
        }

        writer.WriteBoolean("turnActive", snapshot.TurnActive);
        writer.WriteString("activity", WireEvents.ActivityName(snapshot.Activity));
        WireWriter.Strings(writer, "queue", snapshot.Queue.Select(ImageMarkerText.Display));
        WireWriter.Todos(writer, "todos", snapshot.Todos);
        writer.WriteStartArray("statusFields");
        foreach (var field in snapshot.StatusFields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            writer.WriteString("text", field.Text);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WireWriter.Usage(writer, "usage", snapshot.Usage);
        WireWriter.Usage(writer, "cumulativeUsage", snapshot.CumulativeUsage);
        writer.WriteNumber("contextWindow", snapshot.ContextWindow);
        if (snapshot.EstimatedContextTokens is { } estimate)
        {
            writer.WriteNumber("estimatedContextTokens", estimate);
        }
        else
        {
            writer.WriteNull("estimatedContextTokens");
        }

        WireWriter.Side(writer, "side", snapshot.Side);
        writer.WriteStartArray("children");
        foreach (var child in snapshot.Children)
        {
            WireWriter.Child(writer, child, full: false);
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
            WireEvents.ApprovalPassedFields(writer, passed);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("interactions");
        foreach (var interaction in interactions)
        {
            if (interaction.Target is null || string.Equals(interaction.Target, client, StringComparison.Ordinal))
            {
                interaction.Write(writer);
            }
        }

        writer.WriteEndArray();
    }
}
