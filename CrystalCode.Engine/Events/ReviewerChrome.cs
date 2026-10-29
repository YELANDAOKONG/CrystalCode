namespace CrystalCode.Engine.Events;

/// <summary>
/// An enabled approval reviewer as the status line shows it. Thinking, tier,
/// speed, and context are display labels such as <c>Think High</c> or
/// <c>Context 200K</c>; a slot the reviewer does not set is blank.
/// </summary>
public sealed record ReviewerChrome(
    string Provider,
    string Model,
    string Thinking,
    string ServiceTier,
    string Speed,
    string Context = "");
