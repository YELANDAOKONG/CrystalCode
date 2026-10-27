using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// The ACP fields of one tool call: title, kind, locations, raw input, and
/// diff content. Shared by <c>tool_call</c> and the tool call inside
/// <c>session/request_permission</c>, so both describe a call the same way.
/// </summary>
internal static class ToolCallWire
{
    public static string Kind(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Name switch
        {
            ReadTool.ToolName or ListTool.ToolName or TreeTool.ToolName or SkillTool.ToolName
                or ViewImageTool.ToolName or DescribeImageTool.ToolName
                or "read_mcp_resource" or "list_mcp_resources" => "read",
            GlobTool.ToolName or GrepTool.ToolName or SearchTool.ToolName => "search",
            EditTool.ToolName or WriteTool.ToolName => "edit",
            ApplyPatchTool.ToolName => PatchKind(call.Arguments),
            BashTool.ToolName or BackgroundBashStartTool.ToolName or BackgroundBashStatusTool.ToolName
                or BackgroundBashLogsTool.ToolName or BackgroundBashStopTool.ToolName => "execute",
            TodoWriteTool.ToolName or TodoReadTool.ToolName => "think",
            _ => "other"
        };
    }

    /// <summary>Writes title, kind, locations, raw input, and diff content.</summary>
    public static void Describe(Utf8JsonWriter writer, ToolCall call, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(call);
        writer.WriteString("title", ToolCallSummary.Text(call.Name, call.Arguments));
        writer.WriteString("kind", Kind(call));
        RawInput(writer, call.Arguments);
        if (workspaceRoot is null)
        {
            return;
        }

        var line = ToolArguments.TryReadOptionalInt32(call.Arguments, "offset", out var offset) && offset is > 0
            ? offset
            : null;
        writer.WriteStartArray("locations");
        foreach (var path in ToolFileChanges.Paths(workspaceRoot, call))
        {
            writer.WriteStartObject();
            writer.WriteString("path", path);
            if (line is { } value)
            {
                writer.WriteNumber("line", value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        var changes = ToolFileChanges.Preview(workspaceRoot, call);
        if (changes.Count == 0)
        {
            return;
        }

        writer.WriteStartArray("content");
        foreach (var change in changes)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "diff");
            writer.WriteString("path", change.Path);
            if (change.OldText is null)
            {
                writer.WriteNull("oldText");
            }
            else
            {
                writer.WriteString("oldText", change.OldText);
            }

            writer.WriteString("newText", change.NewText);
            if (change.Delete || change.MoveFrom is not null)
            {
                writer.WriteStartObject("_meta");
                writer.WriteStartObject("crystal");
                if (change.Delete)
                {
                    writer.WriteBoolean("delete", true);
                }

                JsonText.Optional(writer, "moveFrom", change.MoveFrom);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>The result text as one text content block, with plain image markers.</summary>
    public static void Result(Utf8JsonWriter writer, ToolResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);
        var text = ImageMarkerText.Display(result.Text);
        writer.WriteString("status", result.Status == ToolResultStatus.Success ? "completed" : "failed");
        writer.WriteStartArray("content");
        writer.WriteStartObject();
        writer.WriteString("type", "content");
        writer.WriteStartObject("content");
        writer.WriteString("type", "text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteStartObject("rawOutput");
        writer.WriteString("status", result.Status.Value);
        writer.WriteString("text", text);
        writer.WriteEndObject();
    }

    private static void RawInput(Utf8JsonWriter writer, string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments.Length == 0 ? "{}" : arguments);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("rawInput");
                document.RootElement.WriteTo(writer);
            }
        }
        catch (JsonException)
        {
            // Text input or a partial argument string has no object form.
        }
    }

    private static string PatchKind(string arguments)
    {
        var sections = ApplyPatchTool.Preview(arguments);
        if (sections.Count == 0)
        {
            return "edit";
        }

        if (sections.All(static section => section.Action == "delete"))
        {
            return "delete";
        }

        return sections.All(static section => section.MovePath is not null && section.AddedLines.Count == 0
            && section.RemovedLines.Count == 0)
            ? "move"
            : "edit";
    }
}
