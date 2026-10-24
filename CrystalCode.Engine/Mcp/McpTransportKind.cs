namespace CrystalCode.Engine.Mcp;

/// <summary>
/// How Crystal talks to one MCP server.
/// </summary>
public enum McpTransportKind
{
    Stdio,
    StreamableHttp,
    Sse
}
