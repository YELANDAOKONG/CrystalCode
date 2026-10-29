namespace CrystalCode.Engine.Events;

/// <summary>
/// A side model the session calls for one job, such as the compaction or
/// image model, as the status line shows it. Thinking, tier, speed, and
/// context are display labels such as <c>Think High</c> or <c>Context 200K</c>;
/// a slot the model does not set for itself is blank.
/// </summary>
public sealed record SideModelChrome(
    string Provider,
    string Model,
    string Thinking,
    string ServiceTier,
    string Speed,
    string Context = "");
