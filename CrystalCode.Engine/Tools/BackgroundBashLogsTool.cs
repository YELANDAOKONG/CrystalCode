using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

public sealed class BackgroundBashLogsTool : ITool
{
    public const string ToolName = "bash_logs";

    private readonly BackgroundTaskRegistry _tasks;

    public BackgroundBashLogsTool(BackgroundTaskRegistry tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _tasks = tasks;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "taskId": {
                      "type": "string",
                      "description": "The task ID returned by bash_start."
                    },
                    "offset": {
                      "type": "integer",
                      "description": "The log offset returned by the previous call."
                    },
                    "limit": {
                      "type": "integer",
                      "description": "Maximum number of characters to return, up to 100000."
                    }
                  },
                  "required": ["taskId"]
                }
                """),
            "Reads buffered output from a background Bash task. "
            + "Use the returned next offset to read later output.");
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        if (!ToolArguments.TryReadRequiredString(call.Arguments, "taskId", out var taskId))
        {
            return ValueTask.FromResult(new ToolOutput(
                "Arguments must include a taskId string.",
                ToolResultStatus.Failure));
        }

        if (!ToolArguments.TryReadOptionalInt32(call.Arguments, "offset", out var offsetValue))
        {
            return ValueTask.FromResult(new ToolOutput(
                "The offset must be an integer.",
                ToolResultStatus.Failure));
        }

        if (!ToolArguments.TryReadOptionalInt32(call.Arguments, "limit", out var limitValue))
        {
            return ValueTask.FromResult(new ToolOutput(
                "The limit must be an integer.",
                ToolResultStatus.Failure));
        }

        var offset = offsetValue ?? 0;
        var limit = limitValue ?? WorkspaceLimits.MaximumBackgroundLogCharacters;
        if (!_tasks.TryReadLogs(taskId, offset, limit, out var log))
        {
            return ValueTask.FromResult(new ToolOutput(log.Text, ToolResultStatus.Failure));
        }

        var text = log.Text.Length == 0 ? "(no new output)" : log.Text;
        text += ToolOutputText.LineSeparator + $"next_offset: {log.NextOffset}";
        if (log.HasMore)
        {
            text += ToolOutputText.LineSeparator + "more_output: true";
        }

        return ValueTask.FromResult(new ToolOutput(text, ToolResultStatus.Success));
    }
}
