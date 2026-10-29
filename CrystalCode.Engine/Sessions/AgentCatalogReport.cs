namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Child Agent settings and the definitions that would load with them.
/// Null limits are unlimited.
/// </summary>
public sealed record AgentCatalogReport(
    bool Enabled,
    int Concurrency,
    int? MaximumModelCalls,
    int? MaximumToolCalls,
    int? MaximumDurationSeconds,
    IReadOnlyList<AgentCatalogEntry> Agents,
    IReadOnlyList<string> Notes);
