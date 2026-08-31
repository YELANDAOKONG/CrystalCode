using Crystal.Tools;

using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Tools;

public sealed class GlobToolTests
{
    [Fact]
    public async Task InvokeAsync_ListsMatchingFiles()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "App.cs"), "class App {}\n");
        File.WriteAllText(Path.Combine(root.Path, "README.md"), "# hi\n");
        var tool = new GlobTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(
            new ToolCall("1", GlobTool.ToolName, """{"pattern":"*.cs"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Equal("App.cs", output.Text);
    }
}
