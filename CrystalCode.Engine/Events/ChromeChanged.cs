namespace CrystalCode.Engine.Events;

/// <summary>
/// Mode, approval, thinking, model, workspace, or prompt set changed.
/// </summary>
public sealed record ChromeChanged(SessionChrome Chrome) : SessionEvent;
