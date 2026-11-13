namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>
/// A parsed, shape-checked checker answer. The host verifies that cited
/// entries exist, not that the judgment is right.
/// </summary>
public sealed record PursueVerdict(
    PursueVerdictKind Kind,
    string Reason,
    string NextStep,
    IReadOnlyList<int> Evidence,
    IReadOnlyList<string> WaitFor)
{
    public static string Token(PursueVerdictKind kind) => kind switch
    {
        PursueVerdictKind.Done => "done",
        PursueVerdictKind.Continue => "continue",
        PursueVerdictKind.Blocked => "blocked",
        PursueVerdictKind.Wait => "wait",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Verdict kind is not supported.")
    };
}
