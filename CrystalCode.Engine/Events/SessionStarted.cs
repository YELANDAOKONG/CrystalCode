namespace CrystalCode.Engine.Events;

/// <summary>
/// The session is open. Carries the initial chrome labels.
/// </summary>
public sealed record SessionStarted(SessionChrome Chrome) : SessionEvent;
