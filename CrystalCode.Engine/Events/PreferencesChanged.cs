namespace CrystalCode.Engine.Events;

/// <summary>
/// A display preference changed or was loaded.
/// </summary>
public sealed record PreferencesChanged(SessionPreferences Preferences) : SessionEvent;
