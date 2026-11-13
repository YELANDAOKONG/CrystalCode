using Crystal;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>
/// One checker call. Exactly one of <see cref="Verdict"/> or a failure is set.
/// </summary>
internal sealed record PursueCheck(
    PursueVerdict? Verdict,
    PursueCheckFailure Failure,
    string Error,
    TokenUsage? Usage,
    int Entries)
{
    public static PursueCheck Verdicted(PursueVerdict verdict, TokenUsage? usage, int entries) =>
        new(verdict, PursueCheckFailure.None, string.Empty, usage, entries);

    public static PursueCheck Failed(PursueCheckFailure failure, string error, TokenUsage? usage = null) =>
        new(null, failure, error, usage, 0);
}
