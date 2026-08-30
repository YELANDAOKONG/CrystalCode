using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Display;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ApprovalCardTests
{
    [Fact]
    public void ReviewLine_KeepsOutcomeRiskAndAuth()
    {
        var verdict = new ApprovalReviewVerdict(
            "allow",
            ReviewRiskLevel.Low,
            ReviewAuthorization.High,
            "Adds the requested test.");

        var line = ApprovalCard.ReviewLine(verdict);

        Assert.Equal("Outcome  Allow  ·  Risk  Low  ·  Auth  High", line);
    }

    [Fact]
    public void ActionLine_UsesPathNotRawJson()
    {
        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");

        var line = ApprovalCard.ActionLine(call);

        Assert.Equal("write  src/App.cs", line);
    }

    [Fact]
    public void PassLines_IncludesHostRiskAuthorityAndReview()
    {
        var classification = new ToolClassification(
            Risk.Write,
            Authority.Workspace,
            "Write workspace file");
        var review = new ApprovalReviewVerdict(
            "allow",
            ReviewRiskLevel.Low,
            ReviewAuthorization.High,
            "Adds the requested test.");

        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");
        var lines = ApprovalCard.PassLines(call, classification, ApprovalPassReason.Review, review);

        Assert.Equal("write  src/App.cs", lines[0]);
        Assert.Equal(
            "Allowed  ·  Review  ·  Risk  Write  ·  Authority  Workspace",
            lines[1]);
        Assert.Contains("Write workspace file", lines);
        Assert.Contains("Outcome  Allow  ·  Risk  Low  ·  Auth  High", lines);
        Assert.Contains("Adds the requested test.", lines);
    }
}
