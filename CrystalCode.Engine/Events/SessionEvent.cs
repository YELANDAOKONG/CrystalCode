namespace CrystalCode.Engine.Events;

/// <summary>
/// One observable change in a coding session. Events are immutable values.
/// A front end projects them onto its own surface; the engine never draws.
/// </summary>
public abstract record SessionEvent
{
    /// <summary>
    /// The front-end client whose request produced this event, or null for
    /// changes nobody in particular asked for (a running turn, a child, a
    /// queued follow-up). Set only through <see cref="CrystalCode.Engine.Sessions.CodingSession.BeginOrigin"/>.
    /// A single-operator front end never sets it.
    /// </summary>
    public string? Origin { get; init; }
}
