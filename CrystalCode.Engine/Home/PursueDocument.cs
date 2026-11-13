using CrystalCode.Engine.Sessions.Pursue;

namespace CrystalCode.Engine.Home;

/// <summary>
/// The saved objective, stop reason, and checker history. Live counters belong
/// to a run; individual check records retain their run and local index.
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
}
