namespace CrystalCode.Engine.Events;

/// <summary>
/// Saved prompts for this workspace, oldest first.
/// </summary>
public sealed record PromptHistoryLoaded(IReadOnlyList<string> Entries) : SessionEvent;
