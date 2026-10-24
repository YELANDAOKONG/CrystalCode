using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Mcp;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpPromptTests
{
    [Fact]
    public async Task Catalog_ListsPromptsAsSlashCommands()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());

        await using var catalog = await LoadAsync("docs", server);

        Assert.Equal(
            ["mcp__docs__review", "mcp__docs__summarize"],
            catalog.Prompts.Select(prompt => prompt.Command).Order().ToArray());
    }

    [Fact]
    public async Task Catalog_KeepsPromptArgumentsAndRequiredness()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("docs", server);

        var prompt = catalog.Prompts.Single(item => item.RemoteName == "review");

        Assert.Equal(
            [("file", true), ("style", false)],
            prompt.Arguments.Select(argument => (argument.Name, argument.Required)).ToArray());
    }

    [Fact]
    public async Task Prompt_ReturnsItsMessagesAsOneUserMessage()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("docs", server);
        var entry = catalog.Prompts.Single(item => item.RemoteName == "review");

        var result = await entry.Client.GetPromptAsync(
            entry.RemoteName,
            new Dictionary<string, object?> { ["file"] = "a.cs" });

        Assert.Equal("Review a.cs", McpPromptText.ToUserMessage(result));
    }

    [Fact]
    public void ToUserMessage_LabelsAssistantMessagesAndNamesOmittedImages()
    {
        var message = McpPromptText.ToUserMessage(new GetPromptResult
        {
            Messages =
            [
                new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = "first" } },
                new PromptMessage { Role = Role.Assistant, Content = new TextContentBlock { Text = "second" } },
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes("AAAA"), MimeType = "image/png" }
                }
            ]
        });

        Assert.Equal("first\nAssistant: second\n[image content omitted: image/png]", message);
    }

    [Fact]
    public async Task ParseArguments_ReadsPairsAndQuotedValues()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("docs", server);
        var entry = catalog.Prompts.Single(item => item.RemoteName == "review");

        var parsed = McpPromptText.TryParseArguments(
            entry,
            """file=src/a.cs style="short and plain" """,
            out var values,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("src/a.cs", values["file"]);
        Assert.Equal("short and plain", values["style"]);
    }

    [Theory]
    [InlineData("style=x", "needs 'file'")]
    [InlineData("file=a.cs bogus=1", "no argument 'bogus'")]
    [InlineData("file=a.cs file=b.cs", "repeated")]
    [InlineData("file", "Usage:")]
    [InlineData("file=\"unterminated", "closing quote")]
    public async Task ParseArguments_RejectsBadInputWithAnExplanation(string text, string expected)
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("docs", server);
        var entry = catalog.Prompts.Single(item => item.RemoteName == "review");

        var parsed = McpPromptText.TryParseArguments(entry, text, out _, out var error);

        Assert.False(parsed);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseArguments_RejectsArgumentsForAPromptWithNone()
    {
        await using var server = await InProcessMcpServer.StartAsync(Features());
        await using var catalog = await LoadAsync("docs", server);
        var entry = catalog.Prompts.Single(item => item.RemoteName == "summarize");

        var parsed = McpPromptText.TryParseArguments(entry, "x=1", out _, out var error);

        Assert.False(parsed);
        Assert.Contains("takes no arguments", error, StringComparison.Ordinal);
    }

    private static InProcessMcpServer.Features Features() =>
        new()
        {
            Prompts =
            [
                McpServerPrompt.Create(
                    string (string file, string? style = null) => $"Review {file}",
                    new McpServerPromptCreateOptions { Name = "review", Description = "Review a file." }),
                McpServerPrompt.Create(
                    () => "Summarize the work.",
                    new McpServerPromptCreateOptions { Name = "summarize" })
            ]
        };

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
