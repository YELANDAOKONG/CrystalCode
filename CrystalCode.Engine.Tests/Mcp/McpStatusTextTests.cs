using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Mcp;

using ModelContextProtocol.Server;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpStatusTextTests
{
    [Fact]
    public void Format_ExplainsHowToTurnOnMcp_WhenDisabled()
    {
        var text = McpStatusText.Format(enabled: false, McpCatalog.Empty);

        Assert.Equal("MCP is off. Use /mcp on to connect configured servers.", text);
    }

    [Fact]
    public void Format_SaysNothingIsConnected_WhenNoServers()
    {
        var text = McpStatusText.Format(enabled: true, McpCatalog.Empty);

        Assert.Equal("MCP is on. No servers are connected.", text);
    }

    [Fact]
    public async Task Format_ListsServerNameSourceTransportStateAndToolCount()
    {
        await using var server = await InProcessMcpServer.StartAsync(
            McpServerTool.Create((string text) => text, new McpServerToolCreateOptions { Name = "echo" }),
            McpServerTool.Create((string text) => text, new McpServerToolCreateOptions { Name = "shout" }));
        var definition = new McpServerDefinition(
            "files",
            McpServerSource.Project,
            McpTransportKind.Stdio,
            command: "unused");
        await using var catalog = await McpCatalog.LoadConnectionsAsync(
            [McpConnection.FromClient(definition, server.Client)],
            McpToolApprovalSettings.Default,
            new HashSet<string>(StringComparer.Ordinal));

        var text = McpStatusText.Format(enabled: true, catalog);

        Assert.Equal(
            "MCP servers" + Environment.NewLine + "  files  Project  stdio  Connected  2 tools",
            text);
    }

    [Fact]
    public async Task Format_NeverShowsArgumentsEnvironmentOrHeaders()
    {
        await using var server = await InProcessMcpServer.StartAsync();
        var definition = new McpServerDefinition(
            "files",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "secret-command",
            arguments: ["--token", "secret-argument"],
            environment: new Dictionary<string, string> { ["KEY"] = "secret-environment" });
        await using var catalog = await McpCatalog.LoadConnectionsAsync(
            [McpConnection.FromClient(definition, server.Client)],
            McpToolApprovalSettings.Default,
            new HashSet<string>(StringComparer.Ordinal));

        var text = McpStatusText.Format(enabled: true, catalog);

        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.Contains("0 tools", text, StringComparison.Ordinal);
    }
}
