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
    public async Task InvokeAsync_ContinuesAfterFilePreflightErrorsAndReportsEveryOutcome()
    {
        using var root = new TemporaryWorkspace();
        var first = Path.Combine(root.Path, "first.txt");
        var failed = Path.Combine(root.Path, "failed.txt");
        var last = Path.Combine(root.Path, "last.txt");
        File.WriteAllText(first, "alpha");
        File.WriteAllText(failed, "alpha");
        File.WriteAllText(last, "alpha");
        var call = Call(
            """
            {"continue_on_error":true,"files":[
              {"path":"first.txt","edits":[{"old_string":"alpha","new_string":"beta"}]},
              {"path":"missing.txt","edits":[{"old_string":"alpha","new_string":"beta"}]},
              {"path":"failed.txt","edits":[
                {"old_string":"alpha","new_string":"beta"},
                {"old_string":"absent","new_string":"gamma"}
              ]},
              {"path":"last.txt","edits":[{"old_string":"alpha","new_string":"gamma"}]}
            ]}
            """);

        var preview = ToolFileChanges.Preview(root.Path, call);
        var result = await new MultiEditTool(new Workspace(root.Path)).InvokeAsync(call);

        Assert.Collection(preview,
            change => Assert.Equal("first.txt", Path.GetFileName(change.Path)),
            change => Assert.Equal("last.txt", Path.GetFileName(change.Path)));
        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.StartsWith("Edited files: 2. Failed files: 2.\n", result.Text, StringComparison.Ordinal);
        Assert.Contains("missing.txt: File not found", result.Text, StringComparison.Ordinal);
        Assert.Contains("failed.txt, edit 2:", result.Text, StringComparison.Ordinal);
        Assert.Contains("Edited first.txt. Replacements: 1.\nEdited last.txt. Replacements: 1.", result.Text, StringComparison.Ordinal);
        Assert.Equal("beta", File.ReadAllText(first));
        Assert.Equal("alpha", File.ReadAllText(failed));
        Assert.Equal("gamma", File.ReadAllText(last));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ContinuesAfterWriteErrorOnlyWhenRequested(bool continueOnError)
    {
        using var root = new TemporaryWorkspace();
        var locked = Path.Combine(root.Path, "locked.txt");
        var last = Path.Combine(root.Path, "last.txt");
        File.WriteAllText(locked, "alpha");
        File.WriteAllText(last, "alpha");
        var call = CallForPaths(continueOnError, "locked.txt", "last.txt");
        using var readHandle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.Equal(2, ToolFileChanges.Preview(root.Path, call).Count);
        var result = await new MultiEditTool(new Workspace(root.Path)).InvokeAsync(call);

        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Contains("locked.txt", result.Text, StringComparison.Ordinal);
        Assert.Contains("The write could not be completed.", result.Text, StringComparison.Ordinal);
        Assert.Equal("alpha", File.ReadAllText(locked));
        Assert.Equal(continueOnError ? "beta" : "alpha", File.ReadAllText(last));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_RejectsDuplicateResolvedPaths(bool continueOnError)
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "file.txt");
        File.WriteAllText(path, "alpha");
        var call = CallForPaths(continueOnError, "file.txt", "./file.txt");

        var result = await new MultiEditTool(new Workspace(root.Path)).InvokeAsync(call);

        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Contains("listed more than once", result.Text, StringComparison.Ordinal);
        Assert.Empty(ToolFileChanges.Preview(root.Path, call));
        Assert.Equal("alpha", File.ReadAllText(path));
    }

    [Fact]
    public async Task InvokeAsync_RejectsNonBooleanContinueOnError()
    {
        using var root = new TemporaryWorkspace();
        var path = Path.Combine(root.Path, "file.txt");
        File.WriteAllText(path, "alpha");
        var call = Call(
            """
            {"continue_on_error":"true","files":[
              {"path":"file.txt","edits":[{"old_string":"alpha","new_string":"beta"}]}
            ]}
            """);

        var result = await new MultiEditTool(new Workspace(root.Path)).InvokeAsync(call);

        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Contains("continue_on_error must be a boolean.", result.Text, StringComparison.Ordinal);
        Assert.Equal("alpha", File.ReadAllText(path));
    }

    [Fact]
    public void Classify_UsesTheRiskiestTarget()
    {
        using var root = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var classifier = new ToolClassifier(new Workspace(root.Path));
        var external = Path.Combine(outside.Path, "file.txt");

        Assert.Equal(Authority.Workspace, classifier.Classify(CallForPaths(false, "a.txt", "b.txt")).Authority);
        Assert.Equal(Authority.OutsideWorkspace, classifier.Classify(CallForPaths(true, "a.txt", external)).Authority);
        Assert.Equal(Risk.Forbidden, classifier.Classify(CallForPaths(true, external, ".ssh/id_rsa")).Risk);
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
        var path = Path.Combine(root.Path, "a.txt");
        File.WriteAllText(path, "alpha");
        var call = CallForPaths(true, "a.txt", Path.Combine(readOnly, "b.txt"));
        var grants = new GrantStore(home.Home);
        grants.Remember(workspace.Root, call, GrantScope.Session);
        var policy = new ApprovalPolicy(ApprovalMode.Edit, workspace, grants, new ThrowingApprovalPrompt());

        var decision = await policy.DecideAsync(call);
        var result = await new MultiEditTool(workspace).InvokeAsync(call);

        Assert.Equal(ToolInvocationAction.Reject, decision.Action);
        Assert.Equal(ToolResultStatus.Failure, result.Status);
        Assert.Contains("read-only", result.Text, StringComparison.Ordinal);
        Assert.Equal("alpha", File.ReadAllText(path));
    }

    private static ToolCall Call(string arguments) =>
        JsonToolCall("1", MultiEditTool.ToolName, arguments);

    private static ToolCall CallForPaths(bool continueOnError, params string[] paths) =>
        Call(JsonSerializer.Serialize(new
        {
            continue_on_error = continueOnError,
            files = paths.Select(path => new
            {
                path,
                edits = new[] { new { old_string = "alpha", new_string = "beta" } }
            })
        }));
}
