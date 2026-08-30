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
}
