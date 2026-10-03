using Crystal.Tools;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The model asked for these tools; they run next.
/// </summary>
public sealed record ToolCallsIssued(IReadOnlyList<ToolCall> Calls) : SessionEvent;
