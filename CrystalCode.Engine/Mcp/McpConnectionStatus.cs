namespace CrystalCode.Engine.Mcp;

/// <summary>
/// The current state of one MCP connection.
/// </summary>
public enum McpConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Failed
}
