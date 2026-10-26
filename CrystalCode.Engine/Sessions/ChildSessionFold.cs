using Crystal;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Folds live child events into a child snapshot. A child document is saved
/// only when an invocation settles, so a front end that shows running
/// children applies usage and todo events to the last saved copy.
/// </summary>
public static class ChildSessionFold
{
    public static bool IsActive(ChildSessionDocument child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return child.Status is "running" or "queued";
    }

    public static ChildSessionDocument Apply(ChildSessionDocument child, SessionEvent value)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            UsageChanged usage => child with
            {
                Usage = Document(usage.Usage),
                CumulativeUsage = Document(usage.CumulativeUsage)
            },
            TodosChanged todos => child with { Todos = SessionMapper.WriteTodos(todos.Items).ToArray() },
            _ => child
        };
    }

    public static TokenUsage? Usage(SessionUsageDocument? usage) => usage is null
        ? null
        : new TokenUsage(usage.InputTokenCount, usage.OutputTokenCount, usage.ReasoningTokenCount);

    public static IReadOnlyList<TodoItem> Todos(ChildSessionDocument child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return SessionMapper.ReadTodos(child.Todos);
    }

    private static SessionUsageDocument? Document(TokenUsage? usage) => usage is null
        ? null
        : new SessionUsageDocument
        {
            InputTokenCount = usage.InputTokenCount,
            OutputTokenCount = usage.OutputTokenCount,
            ReasoningTokenCount = usage.ReasoningTokenCount
        };
}
