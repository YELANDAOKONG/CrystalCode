using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Display;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ApprovalCardTests
{
    [Fact]
    public void ReviewLine_MatchesCodexAssessmentOrder()
    {
        var verdict = new ApprovalReviewVerdict(
            "allow",
            ReviewRiskLevel.Low,
            ReviewAuthorization.High,
            "Adds the requested test.");

        var line = ApprovalCard.ReviewLine(verdict);

        Assert.Equal("allow  risk low  auth high", line);
    }

    [Fact]
    public void ActionLine_KeepsToolNameAndCompactsArguments()
    {
        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");

        var line = ApprovalCard.ActionLine(call);

        Assert.StartsWith("write  ", line, StringComparison.Ordinal);
        Assert.Contains("src/App.cs", line, StringComparison.Ordinal);
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

        Assert.StartsWith("write  ", lines[0], StringComparison.Ordinal);
        Assert.Equal("auto  review  risk write  auth workspace", lines[1]);
        Assert.Contains("Write workspace file", lines);
        Assert.Contains("allow  risk low  auth high", lines);
        Assert.Contains("Adds the requested test.", lines);
    }
}
