using CrystalCode.Engine.Sessions.Pursue;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The pursued objective or its progress changed. Null means there is no
/// objective now.
/// </summary>
public sealed record PursueChanged(PursueState? Pursue) : SessionEvent;
