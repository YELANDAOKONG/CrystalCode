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

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        if (!ToolArguments.TryReadRequiredString(call.Arguments, "taskId", out var taskId))
        {
            return new ToolOutput(
                "Arguments must include a taskId string.",
                ToolResultStatus.Failure);
        }

        var snapshot = await _tasks.StopAsync(taskId);
        if (snapshot is null)
        {
            return new ToolOutput(
                "The background task was not found in this session.",
                ToolResultStatus.Failure);
        }

        return new ToolOutput(
            BackgroundTaskText.Snapshot(snapshot),
            ToolResultStatus.Success);
    }
}
