using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>
/// A model request failed and will be repeated after a wait.
/// </summary>
public sealed record RetryScheduled(SessionRetryAttempt Attempt) : SessionEvent;
