namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Where an MCP server definition came from. A later source replaces an
/// earlier one with the same name: Home, then Project, then Session.
/// </summary>
public enum McpServerSource
{
    Home,
    Project,
    Session
}
