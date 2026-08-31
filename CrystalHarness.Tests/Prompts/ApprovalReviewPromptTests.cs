using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Prompts;
using CrystalHarness.Tests.Tools;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Prompts;

public sealed class ApprovalReviewPromptTests
{
    [Fact]
    public void UserText_RequiresAndLeadsWithUserRequest()
    {
        using var root = new TemporaryWorkspace();
        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");
        var classification = new ToolClassifier(new Workspace(root.Path)).Classify(call);

        var text = ApprovalReviewPrompt.UserText(
            new ApprovalReviewRequest(call, classification, "Add a failing test."));

        Assert.StartsWith("## User request", text, StringComparison.Ordinal);
        Assert.Contains("Add a failing test.", text, StringComparison.Ordinal);
        Assert.Contains("## Proposed action", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UserText_ThrowsWhenUserRequestIsMissing()
    {
        using var root = new TemporaryWorkspace();
        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");
        var classification = new ToolClassifier(new Workspace(root.Path)).Classify(call);

        Assert.Throws<ArgumentException>(() =>
            ApprovalReviewPrompt.UserText(
                new ApprovalReviewRequest(call, classification, " ")));
    }

    [Fact]
    public void SystemText_ForbidsAllowingForbiddenActions()
    {
        Assert.Contains(
            "Forbidden actions must not be allowed.",
            ApprovalReviewPrompt.SystemText,
            StringComparison.Ordinal);
    }
}
