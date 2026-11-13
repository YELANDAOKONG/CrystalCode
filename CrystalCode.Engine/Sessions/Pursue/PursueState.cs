namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>
/// One pursued objective and how far the current run has got. The counts are
/// per run: a resume starts them again. <see cref="ReasonCode"/> is a stable
/// token and <see cref="Reason"/> is English text for people.
/// </summary>
public sealed record PursueState(
    string Objective,
    PursueStatus Status,
    PursuePhase Phase = PursuePhase.None,
    string ReasonCode = "",
    string Reason = "",
    int Checks = 0,
    int Continuations = 0,
    int Stalls = 0,
    string NextStep = "")
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string RunId { get; init; } = string.Empty;

    public IReadOnlyList<PursueCheckRecord> History { get; init; } = [];

    public const int MaximumObjectiveCharacters = 8000;

    public static PursueState Start(string objective) =>
        new(objective, PursueStatus.Active, PursuePhase.Working);

    public PursueState Active(PursuePhase phase) =>
        this with { Status = PursueStatus.Active, Phase = phase, ReasonCode = string.Empty, Reason = string.Empty };

    public PursueState Stopped(PursueStatus status, string reasonCode, string reason) =>
        this with { Status = status, Phase = PursuePhase.None, ReasonCode = reasonCode, Reason = reason };

    public static string StatusToken(PursueStatus status) => status switch
    {
        PursueStatus.Active => "active",
        PursueStatus.Paused => "paused",
        PursueStatus.Blocked => "blocked",
        PursueStatus.Complete => "complete",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Pursue status is not supported.")
    };

    public static string PhaseToken(PursuePhase phase) => phase switch
    {
        PursuePhase.None => "none",
        PursuePhase.Working => "working",
        PursuePhase.Checking => "checking",
        PursuePhase.Waiting => "waiting",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Pursue phase is not supported.")
    };
}
