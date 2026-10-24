namespace CrystalCode.Engine.Mcp;

/// <summary>
/// One server read from a configuration file. A null definition means the
/// entry was invalid or ambiguous; it still takes the name so a Project entry
/// replaces a Home entry even when it cannot be used.
/// </summary>
internal sealed record McpConfigEntry(
    string Name,
    McpServerDefinition? Definition,
    string Origin);
