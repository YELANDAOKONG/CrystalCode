using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

public sealed class BackgroundBashStatusTool : ITool
{
    public const string ToolName = "bash_status";

    private readonly BackgroundTaskRegistry _tasks;

    public BackgroundBashStatusTool(BackgroundTaskRegistry tasks)
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
            "Reports the status of a background Bash task from this session.");
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

        if (!_tasks.TryGetSnapshot(taskId, out var snapshot))
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
