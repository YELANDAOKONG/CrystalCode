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
}
