using Crystal.Tools;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A tool batch finished.
/// </summary>
public sealed record ToolResultsReceived(IReadOnlyList<ToolResult> Results) : SessionEvent;
