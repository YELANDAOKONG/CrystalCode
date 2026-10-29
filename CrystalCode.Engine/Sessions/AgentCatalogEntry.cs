namespace CrystalCode.Engine.Sessions;

/// <summary>
/// One child Agent definition. A null model field inherits the parent's
/// value. <see cref="Tools"/> is null when the Agent uses the default set.
/// </summary>
public sealed record AgentCatalogEntry(
    string Name,
    string Description,
    bool Custom,
    string? Provider,
    string? Model,
    string? Thinking,
    int? Context,
    string? Tier,
    string? Speed,
    IReadOnlyList<string>? Tools);
