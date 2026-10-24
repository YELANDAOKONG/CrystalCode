using Crystal.Tools;

using CrystalCode.Engine.Mcp;

using ModelContextProtocol.Protocol;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpToolResultTextTests
{
    [Fact]
    public void ToOutput_ReturnsTextAsSuccess()
    {
        var output = McpToolResultText.ToOutput(new CallToolResult
        {
            Content = [new TextContentBlock { Text = "hello" }]
        });

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("hello", output.Text);
    }

    [Fact]
    public void ToOutput_MapsServerErrorToFailure()
    {
        var output = McpToolResultText.ToOutput(new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "no such file" }]
        });

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("no such file", output.Text);
    }

    [Fact]
    public void ToOutput_JoinsBlocksOnSeparateLines()
    {
        var output = McpToolResultText.ToOutput(new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "one" },
                new TextContentBlock { Text = "two" }
            ]
        });

        Assert.Equal("one\ntwo", output.Text);
    }

    [Fact]
    public void ToOutput_ReportsEmptyResult()
    {
        var output = McpToolResultText.ToOutput(new CallToolResult { Content = [] });

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal(McpToolResultText.EmptyResult, output.Text);
    }

    [Fact]
    public void ToOutput_NamesOmittedImageInsteadOfSendingBytes()
    {
        var output = McpToolResultText.ToOutput(new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "screenshot" },
                new ImageContentBlock { Data = System.Text.Encoding.UTF8.GetBytes("AAAA"), MimeType = "image/png" }
            ]
        });

        Assert.Contains("screenshot", output.Text, StringComparison.Ordinal);
        Assert.Contains("[image content omitted: image/png]", output.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAA", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToOutput_UsesStructuredContent_WhenNoTextWasReturned()
    {
        var output = McpToolResultText.ToOutput(new CallToolResult
        {
            Content = [],
            StructuredContent = System.Text.Json.JsonDocument.Parse("""{"count":3}""").RootElement.Clone()
        });

        Assert.Equal("""{"count":3}""", output.Text);
    }

    [Fact]
    public void ToOutput_DescribesResourceLinkAndBinaryResource()
    {
        var output = McpToolResultText.ToOutput(new CallToolResult
        {
            Content =
            [
                new ResourceLinkBlock { Uri = "file:///a.txt", Name = "a" },
                new EmbeddedResourceBlock
                {
                    Resource = new BlobResourceContents
                    {
                        Uri = "file:///b.bin",
                        MimeType = "application/octet-stream",
                        Blob = System.Text.Encoding.UTF8.GetBytes("QUJD")
                    }
                }
            ]
        });

        Assert.Contains("[resource link: file:///a.txt (a)]", output.Text, StringComparison.Ordinal);
        Assert.Contains("[binary resource omitted: file:///b.bin (application/octet-stream)]", output.Text, StringComparison.Ordinal);
    }
}
