namespace CrystalCode.Engine.Mcp;

/// <summary>
/// One configured MCP server as the inventory shows it. It carries the
/// transport and where the definition lives, never arguments, environment,
/// headers, or the URL, because those can hold secrets.
/// </summary>
/// <param name="Effective">
/// False when a Project server of the same name replaces this Home server.
/// </param>
/// <param name="Transport">Null when the definition is invalid.</param>
/// <param name="Enabled">Null when the definition is invalid.</param>
public sealed record McpServerEntry(
    string Name,
    McpServerSource Source,
    string Path,
    bool Effective,
    McpTransportKind? Transport,
    bool? Enabled,
    string Catalogs,
    string Error);
