using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home;

internal sealed record AgentManifest
{
    public string? Description { get; init; }
    public bool Enabled { get; init; }
    public string[]? Tools { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Thinking { get; init; }
    public int? Context { get; init; }
    public string? Tier { get; init; }
    public string? Speed { get; init; }

    public AgentModelSettings ModelSettings() => new()
    {
        Provider = Provider, Model = Model, Thinking = Thinking,
        Context = Context, Tier = Tier, Speed = Speed
    };
}
