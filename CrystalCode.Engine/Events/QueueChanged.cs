namespace CrystalCode.Engine.Events;

/// <summary>
/// Follow-up messages waiting for the current turn, oldest first.
/// </summary>
public sealed record QueueChanged(IReadOnlyList<string> Items) : SessionEvent;
