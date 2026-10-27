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

    /// <summary>
    /// Copies the limits with one field replaced. <paramref name="name"/> is
    /// <c>model-calls</c>, <c>tool-calls</c>, or <c>duration</c>; a null value
    /// clears that field.
    /// </summary>
    public AgentLimits With(string name, int? value) => name switch
    {
        "model-calls" => new AgentLimits(value, MaximumToolCalls, MaximumDurationSeconds),
        "tool-calls" => new AgentLimits(MaximumModelCalls, value, MaximumDurationSeconds),
        "duration" => new AgentLimits(MaximumModelCalls, MaximumToolCalls, value),
        _ => throw new ArgumentException("Unknown Agent limit. Use model-calls, tool-calls, or duration.", nameof(name))
    };

    public TurnLimits ToTurnLimits() =>
        new(
            MaximumModelCalls,
            MaximumToolCalls,
            MaximumDurationSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : null);
}
