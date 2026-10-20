namespace CrystalCode.Engine.Events;

public sealed record ChildEventReceived(string ChildSessionId, Guid InvocationId, SessionEvent Event) : SessionEvent;
