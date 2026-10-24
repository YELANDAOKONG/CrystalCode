using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Mcp;

using ModelContextProtocol.Server;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpToolTests
{
    [Fact]
    public async Task InvokeAsync_FailsWithTimeoutText_WhenServerDoesNotAnswerInTime()
    {
        await using var server = await InProcessMcpServer.StartAsync(Hanging());
        await using var catalog = await LoadAsync(server, timeoutSeconds: 1);
        var tool = Assert.Single(catalog.WorkTools);

        var output = await tool.InvokeAsync(JsonToolCall("1", "mcp__test__hang", "{}"));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("1 second", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_Throws_WhenCallerCancels()
    {
        await using var server = await InProcessMcpServer.StartAsync(Hanging());
        await using var catalog = await LoadAsync(server, timeoutSeconds: null);
        var tool = Assert.Single(catalog.WorkTools);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await tool.InvokeAsync(JsonToolCall("1", "mcp__test__hang", "{}"), cancellation.Token));
    }

    [Fact]
    public async Task InvokeAsync_ReportsServerErrorAsFailure_WithoutLeakingDetail()
    {
        var failing = McpServerTool.Create(
            string () => throw new InvalidOperationException("disk is full"),
            new McpServerToolCreateOptions { Name = "fail" });
        await using var server = await InProcessMcpServer.StartAsync(failing);
        await using var catalog = await LoadAsync(server, timeoutSeconds: 30);
        var tool = Assert.Single(catalog.WorkTools);

        var output = await tool.InvokeAsync(JsonToolCall("1", "mcp__test__fail", "{}"));

        // The SDK server hides the exception message from the client on purpose.
        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("An error occurred invoking 'fail'", output.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("disk is full", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Definition_UsesModelNameAndServerDescription()
    {
        await using var server = await InProcessMcpServer.StartAsync(Hanging());
        await using var catalog = await LoadAsync(server, timeoutSeconds: 30);

        var definition = Assert.Single(catalog.WorkTools).Definition;

        Assert.Equal("mcp__test__hang", definition.Name);
        Assert.Equal("Waits until canceled.", definition.Description);
    }

    private static McpServerTool Hanging() =>
        McpServerTool.Create(
            async (CancellationToken token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return "never";
            },
            new McpServerToolCreateOptions { Name = "hang", Description = "Waits until canceled." });

    private static Task<McpCatalog> LoadAsync(InProcessMcpServer server, int? timeoutSeconds)
    {
        var definition = new McpServerDefinition(
            "test",
            McpServerSource.Home,
            McpTransportKind.Stdio,
            command: "unused",
            toolTimeoutSeconds: timeoutSeconds);
        return McpCatalog.LoadConnectionsAsync(
            [McpConnection.FromClient(definition, server.Client)],
            McpToolApprovalSettings.Default,
            new HashSet<string>(StringComparer.Ordinal));
    }
}
