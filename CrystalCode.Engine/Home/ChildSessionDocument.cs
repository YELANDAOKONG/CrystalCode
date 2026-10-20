using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home;

/// <summary>Child state is embedded in the parent; it is never a saved top-level session.</summary>
public sealed record ChildSessionDocument
{
    public string Id { get; init; } = string.Empty;
    public string ParentCallId { get; init; } = string.Empty;
    public string Agent { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Status { get; init; } = "interrupted";
    public string? StopReason { get; init; }
    public string SystemPrompt { get; init; } = string.Empty;
    public string[] Tools { get; init; } = [];
    public AgentModelSettings Model { get; init; } = new();
    public SessionItemDocument[] Items { get; init; } = [];
    public SessionItemDocument[] Archive { get; init; } = [];
    public SessionTodoDocument[] Todos { get; init; } = [];
    public SessionImageDocument[] Images { get; init; } = [];
    public SessionUsageDocument? Usage { get; init; }
    public SessionUsageDocument? CumulativeUsage { get; init; }
    public int ModelCalls { get; init; }
    public int ToolCalls { get; init; }
    public string[] Notes { get; init; } = [];
}
