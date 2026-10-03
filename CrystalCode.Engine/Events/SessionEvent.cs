namespace CrystalCode.Engine.Events;

/// <summary>
/// One observable change in a coding session. Events are immutable values.
/// A front end projects them onto its own surface; the engine never draws.
/// </summary>
public abstract record SessionEvent;
