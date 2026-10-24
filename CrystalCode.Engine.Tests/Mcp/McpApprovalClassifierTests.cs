using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Mcp;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Mcp;

public sealed class McpApprovalClassifierTests
{
    private static readonly IReadOnlySet<string> Tools = new HashSet<string>(StringComparer.Ordinal)
    {
        "mcp__files__read",
        "mcp__files__write"
    };

    private static readonly IReadOnlySet<string> ReadOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        "mcp__files__read"
    };

    [Fact]
    public void TryClassify_ReturnsFalse_ForToolsItDoesNotOwn()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new McpApprovalClassifier(Tools, ReadOnly);

        var matched = classifier.TryClassify(Call("bash"), new Workspace(root.Path), out _);

        Assert.False(matched);
    }

    [Fact]
    public void TryClassify_TreatsUndeclaredToolAsWorkspaceWrite()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new McpApprovalClassifier(Tools, ReadOnly);

        var matched = classifier.TryClassify(Call("mcp__files__write"), new Workspace(root.Path), out var result);

        Assert.True(matched);
        Assert.Equal(Risk.Write, result.Risk);
        Assert.Equal(Authority.Workspace, result.Authority);
        Assert.False(result.RequirePrompt);
    }

    [Fact]
    public void TryClassify_TreatsTrustedReadOnlyToolAsRead()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new McpApprovalClassifier(Tools, ReadOnly);

        var matched = classifier.TryClassify(Call("mcp__files__read"), new Workspace(root.Path), out var result);

        Assert.True(matched);
        Assert.Equal(Risk.Read, result.Risk);
        Assert.Equal(Authority.Workspace, result.Authority);
    }

    [Fact]
    public void TryClassify_SummaryNamesTheToolWithoutArguments()
    {
        using var root = new TemporaryWorkspace();
        var classifier = new McpApprovalClassifier(Tools, ReadOnly);

        classifier.TryClassify(
            JsonToolCall("1", "mcp__files__write", """{"token":"sk-secret"}"""),
            new Workspace(root.Path),
            out var result);

        Assert.DoesNotContain("sk-secret", result.Summary, StringComparison.Ordinal);
    }

    private static ToolCall Call(string name) => JsonToolCall("1", name, "{}");
}
