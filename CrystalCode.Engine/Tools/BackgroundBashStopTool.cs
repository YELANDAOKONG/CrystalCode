using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

public sealed class BackgroundBashStopTool : ITool
{
    public const string ToolName = "bash_stop";

    private readonly BackgroundTaskRegistry _tasks;

    public BackgroundBashStopTool(BackgroundTaskRegistry tasks)
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
                    }
                  },
                  "required": ["taskId"]
                }
                """),
            "Stops a background Bash task and its child processes.");
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

        if (!_tasks.TryStop(taskId, out var snapshot))
        {
            return ValueTask.FromResult(new ToolOutput(
                "The background task was not found in this session.",
                ToolResultStatus.Failure));
        }

        return ValueTask.FromResult(new ToolOutput(
            BackgroundTaskText.Snapshot(snapshot),
            ToolResultStatus.Success));
    }
}
