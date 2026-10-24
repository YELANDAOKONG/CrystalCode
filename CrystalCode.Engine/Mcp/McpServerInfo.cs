namespace CrystalCode.Engine.Mcp;

/// <summary>
/// What a front end shows about one MCP server. It carries no command
/// arguments, environment, or headers, because those can hold secrets.
/// </summary>
public sealed record McpServerInfo(
    string Name,
    McpServerSource Source,
    McpTransportKind Transport,
    McpConnectionStatus Status,
    int ToolCount,
    string Error);
