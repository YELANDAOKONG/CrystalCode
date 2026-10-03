namespace CrystalCode.Engine.Events;

/// <summary>
/// Prompt-history entries that referenced image attachments are no longer valid.
/// </summary>
public sealed record ImageHistoryInvalidated : SessionEvent;
