using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Mcp;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpResourceToolsTests
{
    [Fact]
    public async Task ResourceTools_AreOffered_OnlyWhenAServerOffersResources()
    {
        await using var without = await InProcessMcpServer.StartAsync();
        await using var withResources = await InProcessMcpServer.StartAsync(Features());

        await using var empty = await LoadAsync("a", without);
        await using var offered = await LoadAsync("a", withResources);

        Assert.Empty(empty.ResourceTools);
        Assert.Equal(
            [McpResourceTools.ListToolName, McpResourceTools.ReadToolName],
            offered.ResourceTools.Select(tool => tool.Definition.Name).Order().ToArray());
    }

    [Fact]
    public async Task List_ShowsUriNameAndType()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("notes", server);

        var output = await Tool(catalog, McpResourceTools.ListToolName)
            .InvokeAsync(JsonToolCall("1", McpResourceTools.ListToolName, """{"server":"notes"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("file:///readme.txt  readme  text/plain", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_ReturnsTheResourceText()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("notes", server);

        var output = await Tool(catalog, McpResourceTools.ReadToolName)
            .InvokeAsync(JsonToolCall(
                "1",
                McpResourceTools.ReadToolName,
                """{"server":"notes","uri":"file:///readme.txt"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("hello from the resource", output.Text);
    }

    [Fact]
    public async Task Read_FailsForAnUnknownServer_AndNamesTheKnownOnes()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("notes", server);

        var output = await Tool(catalog, McpResourceTools.ReadToolName)
            .InvokeAsync(JsonToolCall(
                "1",
                McpResourceTools.ReadToolName,
                """{"server":"other","uri":"file:///readme.txt"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("'other'", output.Text, StringComparison.Ordinal);
        Assert.Contains("notes", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_FailsWhenTheServerRejectsTheUri()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("notes", server);

        var output = await Tool(catalog, McpResourceTools.ReadToolName)
            .InvokeAsync(JsonToolCall(
                "1",
                McpResourceTools.ReadToolName,
                """{"server":"notes","uri":"file:///missing.txt"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
    }

    [Theory]
    [InlineData(McpResourceTools.ListToolName, "{}")]
    [InlineData(McpResourceTools.ReadToolName, """{"server":"notes"}""")]
    public async Task Invoke_FailsWhenAnArgumentIsMissing(string tool, string arguments)
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("notes", server);

        var output = await Tool(catalog, tool).InvokeAsync(JsonToolCall("1", tool, arguments));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("is required", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResourceToolNames_AreNotUsableByAServerTool()
    {
        var clash = McpServerTool.Create(
            (string text) => text,
            new McpServerToolCreateOptions { Name = "list_mcp_resources" });
        await using var server = await InProcessMcpServer.StartAsync(clash);

        await using var catalog = await LoadAsync("notes", server);

        Assert.Equal("mcp__notes__list_mcp_resources", Assert.Single(catalog.Tools).ModelName);
    }

    private static InProcessMcpServer.Features Features() =>
        new()
        {
            Resources =
            [
                McpServerResource.Create(
                    () => "hello from the resource",
                    new McpServerResourceCreateOptions
                    {
                        UriTemplate = "file:///readme.txt",
                        Name = "readme",
                        MimeType = "text/plain"
                    })
            ]
        };

    private static ITool Tool(McpCatalog catalog, string name) =>
        catalog.ResourceTools.Single(tool => tool.Definition.Name == name);

    private static Task<McpCatalog> LoadAsync(string name, InProcessMcpServer server)
    {
        var definition = new McpServerDefinition(
            name,
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "unused");
        return McpCatalog.LoadConnectionsAsync(
            [McpConnection.FromClient(definition, server.Client)],
            McpToolApprovalSettings.Default,
            new HashSet<string>(StringComparer.Ordinal));
    }
}
