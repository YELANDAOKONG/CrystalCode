using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

internal sealed class TaskTool : ITool
{
    public const string ToolName = "task";
    private readonly Func<string, string, string, string?, string, CancellationToken, Task<string>> _invoke;

    public TaskTool(string agents, Func<string, string, string, string?, string, CancellationToken, Task<string>> invoke)
    {
        _invoke = invoke;
        Definition = ToolDefinition.Json(ToolName, ToolSchema.Parse("""
            {"type":"object","properties":{
              "agent":{"type":"string"},"label":{"type":"string"},
              "prompt":{"type":"string"},"sessionId":{"type":"string"}},
              "required":["agent","label","prompt"],"additionalProperties":false}
            """),
            "Delegate a bounded task to a child session with fresh context. Include all necessary context, "
            + "the permitted change scope, and the expected report. Without sessionId, creates a new child. "
            + "With sessionId, continues that child. Calls wait for completion; adjacent task calls may run in parallel. "
            + "Give parallel writers non-overlapping files. Available Agents:\n" + agents);
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        using var json = JsonDocument.Parse(call.Arguments);
        var root = json.RootElement;
        var agent = Required(root, "agent");
        var label = Required(root, "label");
        var prompt = Required(root, "prompt");
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("agent" or "label" or "prompt" or "sessionId"))
            {
                throw new ArgumentException("Unknown task argument.", nameof(call));
            }
        }
        var id = root.TryGetProperty("sessionId", out var value) ? value.GetString() : null;
        var text = await _invoke(agent, label, prompt, id, call.CallId, cancellationToken);
        using var result = JsonDocument.Parse(text);
        var completed = result.RootElement.GetProperty("status").GetString() == "completed";
        return new ToolOutput(text, completed ? ToolResultStatus.Success : ToolResultStatus.Failure);
    }

    private static string Required(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException($"Task requires a non-empty {name}.", nameof(root));
        }
        return value.GetString()!;
    }
}
