using Crystal;

namespace CrystalCode.Engine.Events;

/// <summary>One checker verdict on the pursued objective.</summary>
public sealed record PursueChecked(
    int Index,
    string Verdict,
    string Reason,
    string NextStep,
    IReadOnlyList<int> Evidence,
    TokenUsage? Usage) : SessionEvent;
