using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class SearchDirectoryAccessTests
{
    [Theory]
    [InlineData("glob")]
    [InlineData("grep")]
    [InlineData("search")]
    public async Task InvokeAsync_AuthorizedAbsolutePathSearchesOnlyRequestedRoot(string name)
    {
        using var root = new TemporaryWorkspace();
        using var requested = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "workspace.txt"), "needle");
        await File.WriteAllTextAsync(Path.Combine(requested.Path, "wanted.txt"), "needle");
        await File.WriteAllTextAsync(Path.Combine(other.Path, "unwanted.txt"), "needle");
        var workspace = new Workspace(root.Path);
        workspace.Access.Set(new Workspace(requested.Path).Root, "read");
        workspace.Access.Set(new Workspace(other.Path).Root, "write");
        ITool tool = name switch
        {
            "glob" => new GlobTool(workspace),
            "grep" => new GrepTool(workspace),
            "search" => new SearchTool(workspace),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var call = JsonToolCall("1", name, JsonSerializer.Serialize(new
        {
            path = requested.Path, pattern = name == "glob" ? "*.txt" : "needle", query = "needle"
        }));

        var classification = new ToolClassifier(workspace).Classify(call);
        var result = await tool.InvokeAsync(call);

        Assert.Equal(Authority.Workspace, classification.Authority);
        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Contains("wanted.txt", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("unwanted.txt", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("workspace.txt", result.Text, StringComparison.Ordinal);
        Assert.Contains("workspace-relative or absolute", tool.Definition.Description, StringComparison.Ordinal);
        Assert.Contains("Session read or write access", tool.Definition.Description, StringComparison.Ordinal);
        Assert.Contains("unlisted outside paths may require approval", tool.Definition.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Full mode", tool.Definition.Description, StringComparison.Ordinal);
        Assert.Contains("only the requested root, not all authorized directories", tool.Definition.Description, StringComparison.Ordinal);
    }
}
