using System.Text;
using System.Text.Json;

using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class MultiEditToolTests
{
    [Fact]
    public async Task InvokeAsync_AppliesOrderedReplacementsAcrossFilesAndPreservesBom()
    {
        using var root = new TemporaryWorkspace();
        var first = Path.Combine(root.Path, "first.txt");
        var second = Path.Combine(root.Path, "second.txt");
        File.WriteAllText(first, "alpha\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(second, "keep remove remove");
        var call = Call(
            """
            {"files":[
              {"path":"first.txt","edits":[
                {"old_string":"alpha","new_string":"beta"},
                {"old_string":"beta","new_string":"gamma"}
              ]},
              {"path":"second.txt","edits":[
                {"old_string":" remove","new_string":"","replace_all":true}
              ]}
            ]}
            """);

        var preview = ToolFileChanges.Preview(root.Path, call);
        var result = await new MultiEditTool(new Workspace(root.Path)).InvokeAsync(call);

        Assert.Equal(ToolResultStatus.Success, result.Status);
        Assert.Collection(preview,
            change => Assert.Equal("gamma\r\n", change.NewText),
            change => Assert.Equal("keep", change.NewText));
        Assert.Equal("gamma\r\n", File.ReadAllText(first));
        Assert.True(File.ReadAllBytes(first).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal("keep", File.ReadAllText(second));
        Assert.Contains("Edited second.txt. Replacements: 2.", result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("repeated")]
    public async Task InvokeAsync_LaterMatchFailureLeavesEveryFileUnchanged(string oldText)
    {
        using var root = new TemporaryWorkspace();
        var first = Path.Combine(root.Path, "first.txt");
        var second = Path.Combine(root.Path, "second.txt");
        File.WriteAllText(first, "alpha");
        File.WriteAllText(second, "repeated repeated");
        var call = Call(
            $$"""
            {"files":[
              {"path":"first.txt","edits":[{"old_string":"alpha","new_string":"beta"}]},
              {"path":"second.txt","edits":[{"old_string":"{{oldText}}","new_string":"changed"}]}
            ]}
            """);

        var result = await new MultiEditTool(new Workspace(root.Path)).InvokeAsync(call);

        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Contains("second.txt, edit 1", result.Text, StringComparison.Ordinal);
        Assert.Empty(ToolFileChanges.Preview(root.Path, call));
        Assert.Equal("alpha", File.ReadAllText(first));
        Assert.Equal("repeated repeated", File.ReadAllText(second));
    }

    [Fact]
    public async Task InvokeAsync_RejectsDuplicateResolvedPaths()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "file.txt");
        File.WriteAllText(path, "alpha");
        var call = CallForPaths("file.txt", "./file.txt");

        var result = await new MultiEditTool(new Workspace(root.Path)).InvokeAsync(call);

        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Contains("listed more than once", result.Text, StringComparison.Ordinal);
        Assert.Equal("alpha", File.ReadAllText(path));
    }

    [Fact]
    public void Classify_UsesTheRiskiestTarget()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var classifier = new ToolClassifier(new Workspace(root.Path));
        var external = Path.Combine(outside.Path, "file.txt");

        Assert.Equal(Authority.Workspace, classifier.Classify(CallForPaths("a.txt", "b.txt")).Authority);
        Assert.Equal(Authority.OutsideWorkspace, classifier.Classify(CallForPaths("a.txt", external)).Authority);
        Assert.Equal(Risk.Forbidden, classifier.Classify(CallForPaths(external, ".ssh/id_rsa")).Risk);
    }

    [Fact]
    public async Task DecideAsync_RejectsReadOnlyTargetBeforeApplyingSessionGrant()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        using var home = new TemporaryHome();
        var workspace = new Workspace(root.Path);
        var readOnly = new Workspace(outside.Path).Root;
        workspace.Access.Set(readOnly, "read");
        var call = CallForPaths("a.txt", Path.Combine(readOnly, "b.txt"));
        var grants = new GrantStore(home.Home);
        grants.Remember(workspace.Root, call, GrantScope.Session);
        var policy = new ApprovalPolicy(ApprovalMode.Edit, workspace, grants, new ThrowingApprovalPrompt());

        var decision = await policy.DecideAsync(call);

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
    }

    private static ToolCall Call(string arguments) =>
        JsonToolCall("1", MultiEditTool.ToolName, arguments);

    private static ToolCall CallForPaths(params string[] paths) =>
        Call(JsonSerializer.Serialize(new
        {
            files = paths.Select(path => new
            {
                path,
                edits = new[] { new { old_string = "alpha", new_string = "beta" } }
            })
        }));
}
