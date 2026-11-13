namespace CrystalCode.Engine.Home;

/// <summary>
/// The saved part of a pursued objective: its text, status token, and the
/// reason it stopped. Counts belong to a run and are not saved.
/// </summary>
public sealed class PursueDocument
{
    public string? Objective { get; set; }

    public string? Status { get; set; }

    public string? ReasonCode { get; set; }

    public string? Reason { get; set; }
}
