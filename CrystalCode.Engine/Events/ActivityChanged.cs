namespace CrystalCode.Engine.Events;

/// <summary>
/// The engine started or finished work outside a streaming round.
/// </summary>
public sealed record ActivityChanged(SessionActivity Activity) : SessionEvent;
