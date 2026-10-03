using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A diagnostic snapshot requested by <c>/status</c>.
/// </summary>
public sealed record StatusReported(SessionStatus Status, bool Full) : SessionEvent;
