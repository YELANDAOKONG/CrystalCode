using CrystalCode.Engine.Sessions.Pursue;

namespace CrystalCode.Engine.Home;

/// <summary>
/// The saved objective, most recent run, and checker history.
/// </summary>
public sealed class PursueDocument
{
    public string? Id { get; set; }

    public string? RunId { get; set; }

    public IReadOnlyList<PursueCheckRecord> History { get; set; } = [];

    public string? Objective { get; set; }

    public string? Status { get; set; }

    public string? ReasonCode { get; set; }

    public string? Reason { get; set; }

    public int? Checks { get; set; }

    public int? Continuations { get; set; }

    public int? Stalls { get; set; }

    public string? NextStep { get; set; }
}
