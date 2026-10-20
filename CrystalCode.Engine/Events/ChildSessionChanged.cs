using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Events;

public sealed record ChildSessionChanged(ChildSessionDocument Child, Guid InvocationId) : SessionEvent;
