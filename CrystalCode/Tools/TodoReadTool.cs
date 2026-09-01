using Crystal.Tools;

namespace CrystalCode.Tools;

/// <summary>
/// Returns the current session todo list without changing it.
/// </summary>
public sealed class TodoReadTool : ITool
{
    internal const string ToolName = "todoread";

    private const string ToolDescription =
        "Reads the current session todo list. Use it to inspect ids, content, and status "
        + "without changing the list. Use todowrite to add, update, or replace items.";

    private readonly TodoList _todos;

    public TodoReadTool(TodoList todos)
    {
        ArgumentNullException.ThrowIfNull(todos);
        _todos = todos;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {}
                }
                """),
            ToolDescription);
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        return ValueTask.FromResult(new ToolOutput(_todos.Format()));
    }
}
