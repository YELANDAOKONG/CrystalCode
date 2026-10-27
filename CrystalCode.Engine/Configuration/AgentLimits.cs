using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Bounds applied to each child Agent run on top of the parent's shared
/// execution budget. A null field is unlimited, and the default sets none.
/// </summary>
public sealed record AgentLimits
{
    public AgentLimits(
        int? maximumModelCalls = null,
        int? maximumToolCalls = null,
        int? maximumDurationSeconds = null)
    {
        MaximumModelCalls = maximumModelCalls;
        MaximumToolCalls = maximumToolCalls;
        MaximumDurationSeconds = maximumDurationSeconds;
        // TurnLimits owns the range rules; reject a bad value when it is read.
        _ = ToTurnLimits();
    }

    public int? MaximumModelCalls { get; }

    public int? MaximumToolCalls { get; }

    public int? MaximumDurationSeconds { get; }

    public TurnLimits ToTurnLimits() =>
        new(
            MaximumModelCalls,
            MaximumToolCalls,
            MaximumDurationSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : null);
}
