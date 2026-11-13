namespace CrystalCode.Engine.Configuration;

/// <summary>Operator-owned model choices for one Agent definition.</summary>
public sealed record AgentModelSettings
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Thinking { get; init; }
    public int? Context { get; init; }
    public string? Tier { get; init; }
    public string? Speed { get; init; }

    /// <summary>
    /// The model that summarizes this Agent's context. Null uses the session
    /// compaction setting. Its own tier, speed, and context follow the same
    /// own-model rules as the Agent model, and its own Compaction is ignored.
    /// </summary>
    public AgentModelSettings? Compaction { get; init; }
}
