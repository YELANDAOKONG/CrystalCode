namespace CrystalCode.Engine.Events;

/// <summary>
/// A side model the session calls for one job, such as the compaction or
/// image model, as the status line shows it. Thinking, tier, and speed are
/// display labels such as <c>Think High</c>; a slot the model does not set
/// for itself is blank.
/// </summary>
public sealed record SideModelChrome(
    string Provider,
    string Model,
    string Thinking,
    string ServiceTier,
    string Speed);
