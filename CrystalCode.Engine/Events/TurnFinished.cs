using Crystal;

using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Events;

/// <summary>
/// The turn ended. Usage values are the session totals after the turn.
/// </summary>
public sealed record TurnFinished(
    TurnResult Result,
    TokenUsage? Usage,
    TokenUsage? CumulativeUsage,
    int ContextWindow) : SessionEvent;
