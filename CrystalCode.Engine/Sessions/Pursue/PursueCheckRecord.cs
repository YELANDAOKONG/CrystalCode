using Crystal;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>A completed checker attempt retained with its objective.</summary>
public sealed record PursueCheckRecord(
    string Id,
    string ObjectiveId,
    string RunId,
    int Index,
    DateTimeOffset CheckedUtc,
    string Verdict,
    string Reason,
    string NextStep,
    IReadOnlyList<PursueEvidence> Evidence,
    IReadOnlyList<string> WaitFor,
    TokenUsage? Usage);
