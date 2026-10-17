using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

public sealed class BackgroundBashStartTool : ITool
{
    public const string ToolName = "bash_start";

    private readonly BackgroundTaskRegistry _tasks;

    public BackgroundBashStartTool(BackgroundTaskRegistry tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _tasks = tasks;
        Definition = ToolDefinition.Json(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "command": {
                      "type": "string",
                      "description": "The exact shell command to start in the background."
                    }
                  },
                  "required": ["command"]
                }
                """),
            "Starts one shell command in the workspace root and returns a session-local task ID. "
            + "Use bash_status or bash_logs to observe it, and bash_stop to end it. "
            + "The configured backgroundBashTimeoutSeconds limit still applies. Background stdin is closed, "
            + "and no more than 32 tasks may be active at once.");
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        if (!BashTool.TryReadCommand(call.Arguments, out var command))
        {
            return ValueTask.FromResult(new ToolOutput(
                "Arguments must include a command string.",
                ToolResultStatus.Failure));
        }

        if (!_tasks.TryStart(command, out var snapshot, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        return ValueTask.FromResult(new ToolOutput(
            BackgroundTaskText.Snapshot(snapshot),
            ToolResultStatus.Success));
    }
}
