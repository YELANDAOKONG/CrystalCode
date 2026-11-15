using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class MultiReadToolTests
{
    [Fact]
    public async Task InvokeAsync_ReadsRepeatedPathInOrderAndContinuesAfterAnError()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "first\nsecond\nthird\n");
        var tool = new MultiReadTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(JsonToolCall(
            "1",
            MultiReadTool.ToolName,
            """
            {"files":[{"path":"a.txt","offset":3},{"path":"missing.txt"},{"path":"a.txt","limit":1}]}
            """));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.StartsWith("[1] a.txt\n     3|third\n\n[2] missing.txt\nError: ", output.Text, StringComparison.Ordinal);
        Assert.Contains("\n\n[3] a.txt\n     1|first", output.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("second", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_CredentialPathRejectsWholeBatch()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(root.Path, "a.txt"), "visible\n");
        var tool = new MultiReadTool(new Workspace(root.Path));

        var output = await tool.InvokeAsync(JsonToolCall(
            "1",
            MultiReadTool.ToolName,
            """{"files":[{"path":"a.txt"},{"path":".ssh/id_rsa"}]}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Equal("Reading credential paths is not allowed.", output.Text);
    }

    [Fact]
    public void Classify_UsesOutsideAuthorityUntilThePathIsAuthorizedAndPrioritizesCredentials()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        var classifier = new ToolClassifier(workspace);
        var outsidePath = Path.Combine(outside.Path, "a.txt");
        var files = new[] { new { path = "local.txt" }, new { path = outsidePath } };
        var call = JsonToolCall("1", MultiReadTool.ToolName, JsonSerializer.Serialize(new { files }));

        var classification = classifier.Classify(call);
        Assert.Equal(Risk.Read, classification.Risk);
        Assert.Equal(Authority.OutsideWorkspace, classification.Authority);

        workspace.Access.Set(new Workspace(outside.Path).Root, "read");
        Assert.Equal(Authority.Workspace, classifier.Classify(call).Authority);

        var blocked = JsonToolCall(
            "2",
            MultiReadTool.ToolName,
            JsonSerializer.Serialize(new { files = files.Append(new { path = ".ssh/id_rsa" }) }));
        Assert.Equal(Risk.Forbidden, classifier.Classify(blocked).Risk);
    }

    [Fact]
    public async Task InvokeAsync_TruncatesLargeRangesAndThenTheBatch()
    {
        using var root = new TemporaryWorkspace();
        File.WriteAllLines(Path.Combine(root.Path, "a.txt"), Enumerable.Repeat(new string('x', 900), 30));
        var tool = new MultiReadTool(new Workspace(root.Path));
        var files = Enumerable.Repeat(new { path = "a.txt" }, 10);

        var output = await tool.InvokeAsync(JsonToolCall(
            "1", MultiReadTool.ToolName, JsonSerializer.Serialize(new { files })));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("[truncated after 22 lines;", output.Text, StringComparison.Ordinal);
        Assert.EndsWith("[truncated to 100000 characters]", output.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[10] a.txt", output.Text, StringComparison.Ordinal);
    }
}
