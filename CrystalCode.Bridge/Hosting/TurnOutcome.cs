using CrystalCode.Engine.Events;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// How one engine turn ended for a waiting prompt. <see cref="Finished"/> is
/// null when the turn faulted before it reported a result, or when the
/// session dropped it (a clear, a resume, a close) before it started.
/// </summary>
internal sealed record TurnOutcome(TurnFinished? Finished, string? Error, bool Abandoned = false);
