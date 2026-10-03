using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class WriteToolTests
{
    [Fact]
    public async Task InvokeAsync_CreatesNestedFile()
    {
        using var root = new TemporaryWorkspace();
        var tool = new WriteTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                WriteTool.ToolName,
                """{"path":"src/App.cs","contents":"class App {}"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("Created src/App.cs (12 characters).", output.Text);
        Assert.Equal("class App {}", File.ReadAllText(Path.Combine(root.Path, "src", "App.cs")));
    }

    [Fact]
    public async Task InvokeAsync_WritesPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var target = Path.Combine(outside.Path, "note.md");
        var tool = new WriteTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                WriteTool.ToolName,
                PathArguments(target, "hello")));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains(target, output.Text, StringComparison.Ordinal);
        Assert.Equal("hello", File.ReadAllText(target));
    }

    [Fact]
    public async Task InvokeAsync_RejectsCredentialPathInsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(root.Path, ".ssh"));
        var key = Path.Combine(root.Path, ".ssh", "id_rsa");
        File.WriteAllText(key, "SECRET");
        var tool = new WriteTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                WriteTool.ToolName,
                """{"path":".ssh/id_rsa","contents":"OVERWRITTEN"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Writing credential paths is not allowed.", output.Text);
        Assert.Equal("SECRET", File.ReadAllText(key));
    }

    [Fact]
    public async Task InvokeAsync_WritesThroughSymlinkThatLeavesTheWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var target = Path.Combine(outside.Path, "secret.txt");
        File.WriteAllText(target, "OUTSIDE");
        File.CreateSymbolicLink(Path.Combine(root.Path, "link.txt"), target);
        var tool = new WriteTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                WriteTool.ToolName,
                """{"path":"link.txt","contents":"PWN"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("PWN", File.ReadAllText(target));
    }

    [Fact]
    public async Task InvokeAsync_RejectsCredentialPathOutsideWorkspace()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var directory = Path.Combine(outside.Path, ".ssh");
        Directory.CreateDirectory(directory);
        var key = Path.Combine(directory, "id_rsa");
        File.WriteAllText(key, "SECRET");
        var tool = new WriteTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall(
                "1",
                WriteTool.ToolName,
                PathArguments(key, "OVERWRITTEN")));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Writing credential paths is not allowed.", output.Text);
        Assert.Equal("SECRET", File.ReadAllText(key));
    }

    private static string PathArguments(string path, string contents) =>
        "{\"path\":" + JsonSerializer.Serialize(path)
        + ",\"contents\":" + JsonSerializer.Serialize(contents) + "}";
}
