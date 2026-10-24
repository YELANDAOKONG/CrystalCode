using System.Text;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Plain text for the <c>/mcp</c> status. It shows names, transports, states,
/// and counts only, because arguments, environment, and headers can hold secrets.
/// </summary>
internal static class McpStatusText
{
    public static string Format(bool enabled, McpCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!enabled)
        {
            return "MCP is off. Use /mcp on to connect configured servers.";
        }

        if (catalog.Servers.Count == 0)
        {
            return "MCP is on. No servers are connected.";
        }

        var builder = new StringBuilder("MCP servers");
        foreach (var server in catalog.Servers)
        {
            builder
                .Append(Environment.NewLine)
                .Append("  ")
                .Append(server.Name)
                .Append("  ")
                .Append(server.Source)
                .Append("  ")
                .Append(Transport(server.Transport))
                .Append("  ")
                .Append(server.Status)
                .Append("  ")
                .Append(server.ToolCount)
                .Append(server.ToolCount == 1 ? " tool" : " tools");
        }

        return builder.ToString();
    }

    private static string Transport(McpTransportKind transport) =>
        transport switch
        {
            McpTransportKind.Stdio => "stdio",
            McpTransportKind.StreamableHttp => "http",
            McpTransportKind.Sse => "sse",
            _ => throw new ArgumentOutOfRangeException(nameof(transport))
        };
}
