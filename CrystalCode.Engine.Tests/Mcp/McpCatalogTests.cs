using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Mcp;

using ModelContextProtocol.Server;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpCatalogTests
{
    [Fact]
    public async Task LoadConnectionsAsync_ListsToolsUnderServerPrefixedNames()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo(readOnly: true));

        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Host);

        var info = Assert.Single(catalog.Servers);
        Assert.Equal(McpConnectionStatus.Connected, info.Status);
        Assert.Equal(1, info.ToolCount);
        Assert.Equal("mcp__test__echo", Assert.Single(catalog.Tools).ModelName);
        Assert.Empty(catalog.Notes);
    }

    [Fact]
    public async Task LoadConnectionsAsync_PutsToolsInWorkCatalogOnly_ByDefault()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo());

        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Host);

        Assert.Single(catalog.WorkTools);
        Assert.Empty(catalog.PlanTools);
    }

    [Fact]
    public async Task LoadConnectionsAsync_PutsToolsInBothCatalogs_WhenServerAsksForBoth()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo());

        await using var catalog = await LoadAsync(
            server,
            ExternalToolTrustPolicy.Host,
            inPlan: true);

        Assert.Single(catalog.WorkTools);
        Assert.Single(catalog.PlanTools);
    }

    [Fact]
    public async Task LoadConnectionsAsync_SkipsTool_WhenNameIsReserved()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo());
        var reserved = new HashSet<string>(StringComparer.Ordinal) { "mcp__test__echo" };

        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Host, reserved: reserved);

        Assert.Empty(catalog.Tools);
        var note = Assert.Single(catalog.Notes);
        Assert.Contains("already in use", note);
    }

    [Fact]
    public async Task LoadConnectionsAsync_TrustsReadOnlyHint_WhenApprovalIsAuthor()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo(readOnly: true));

        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Author);

        Assert.True(Assert.Single(catalog.Tools).ReadOnly);
    }

    [Fact]
    public async Task LoadConnectionsAsync_IgnoresReadOnlyHint_WhenApprovalIsHost()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo(readOnly: true));

        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Host);

        Assert.False(Assert.Single(catalog.Tools).ReadOnly);
    }

    [Fact]
    public async Task LoadConnectionsAsync_IgnoresMissingHint_WhenApprovalIsAuthor()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo());

        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Author);

        Assert.False(Assert.Single(catalog.Tools).ReadOnly);
    }

    [Fact]
    public async Task LoadConnectionsAsync_ConnectsServerWithNoTools()
    {
        await using var server = await InProcessMcpServer.StartAsync();

        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Host);

        Assert.Equal(McpConnectionStatus.Connected, Assert.Single(catalog.Servers).Status);
        Assert.Empty(catalog.Tools);
        Assert.Empty(catalog.Notes);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsServerText()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo());
        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Host);
        var tool = Assert.Single(catalog.WorkTools);

        var output = await tool.InvokeAsync(
            JsonToolCall("1", "mcp__test__echo", """{"text":"hello"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("Echoed: hello", output.Text);
    }

    [Fact]
    public async Task InvokeAsync_Fails_WhenArgumentsAreNotAnObject()
    {
        await using var server = await InProcessMcpServer.StartAsync(Echo());
        await using var catalog = await LoadAsync(server, ExternalToolTrustPolicy.Host);
        var tool = Assert.Single(catalog.WorkTools);

        var output = await tool.InvokeAsync(JsonToolCall("1", "mcp__test__echo", "[1,2]"));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("JSON object", output.Text);
    }

    private static McpServerTool Echo(bool readOnly = false) =>
        McpServerTool.Create(
            (string text) => $"Echoed: {text}",
            new McpServerToolCreateOptions
            {
                Name = "echo",
                Description = "Returns the input text.",
                ReadOnly = readOnly
            });

    private static Task<McpCatalog> LoadAsync(
        InProcessMcpServer server,
        ExternalToolTrustPolicy trust,
        bool inPlan = false,
        IReadOnlySet<string>? reserved = null)
    {
        var definition = new McpServerDefinition(
            "test",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "unused",
            inPlan: inPlan);
        return McpCatalog.LoadConnectionsAsync(
            [McpConnection.FromClient(definition, server.Client)],
            new McpToolApprovalSettings(trust, trust),
            reserved ?? new HashSet<string>(StringComparer.Ordinal));
    }
}
