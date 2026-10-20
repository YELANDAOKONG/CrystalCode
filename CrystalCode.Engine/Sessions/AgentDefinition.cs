using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions;

internal sealed record AgentDefinition(
    string Name,
    string Description,
    string Prompt,
    AgentModelSettings Model,
    IReadOnlyList<string>? Tools = null,
    string? ManifestPath = null);
