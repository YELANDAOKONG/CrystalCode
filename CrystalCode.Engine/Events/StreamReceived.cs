using Crystal.Chat;

namespace CrystalCode.Engine.Events;

/// <summary>
/// One delta from the streaming model round.
/// </summary>
public sealed record StreamReceived(ChatStreamEvent StreamEvent) : SessionEvent;
