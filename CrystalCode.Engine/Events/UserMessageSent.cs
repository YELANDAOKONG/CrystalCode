namespace CrystalCode.Engine.Events;

/// <summary>
/// A user message was accepted and the turn is about to start.
/// Text may contain trusted image markers; see <c>ImageMarkerText</c>.
/// </summary>
public sealed record UserMessageSent(string Text) : SessionEvent;
