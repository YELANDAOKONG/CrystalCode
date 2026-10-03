namespace CrystalCode.Engine.Events;

/// <summary>
/// The streaming model round ended, with or without tool calls.
/// </summary>
public sealed record ModelRoundClosed : SessionEvent;
