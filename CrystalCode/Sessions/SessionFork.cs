using CrystalCode.Home;

namespace CrystalCode.Sessions;

/// <summary>
/// Creates an independent persisted branch from a session document.
/// </summary>
internal static class SessionFork
{
    public static SessionDocument Create(
        SessionDocument source,
        string id,
        string workspaceRoot,
        DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return new SessionDocument
        {
            Id = id.Trim(),
            Workspace = Path.GetFullPath(workspaceRoot),
            PlanMode = source.PlanMode,
            CreatedUtc = createdUtc,
            Items = source.Items.Select(CloneItem).ToList(),
            Todos = source.Todos.Select(CloneTodo).ToList(),
            UserTurns = Math.Max(0, source.UserTurns),
            ModelCalls = Math.Max(0, source.ModelCalls),
            ToolCalls = Math.Max(0, source.ToolCalls),
            Usage = CloneUsage(source.Usage)
        };
    }

    private static SessionItemDocument CloneItem(SessionItemDocument item) =>
        new()
        {
            Kind = item.Kind,
            Role = item.Role,
            Text = item.Text,
            CallId = item.CallId,
            Name = item.Name,
            Arguments = item.Arguments,
            Status = item.Status
        };

    private static SessionTodoDocument CloneTodo(SessionTodoDocument todo) =>
        new()
        {
            Id = todo.Id,
            Content = todo.Content,
            Status = todo.Status
        };

    private static SessionUsageDocument? CloneUsage(SessionUsageDocument? usage) =>
        usage is null
            ? null
            : new SessionUsageDocument
            {
                InputTokenCount = usage.InputTokenCount,
                OutputTokenCount = usage.OutputTokenCount,
                ReasoningTokenCount = usage.ReasoningTokenCount
            };
}
