namespace CrystalCode.Engine.Events;

/// <summary>
/// Mode, approval, thinking, model, workspace, prompt set, or selected
/// local context budget changed.
/// </summary>
public sealed record ChromeChanged(SessionChrome Chrome) : SessionEvent;
