using Crystal.Tools;

using CrystalCode.Approvals;
using CrystalCode.Display.Paint;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Approvals;

public sealed class ApprovalCardTests
{
    [Fact]
    public void Field_UsesTitleCaseLabelsAndValues()
    {
        Assert.Equal("Outcome  Allow", ApprovalCard.Field("Outcome", "allow"));
        Assert.Equal("Authority  High", ApprovalCard.Field("Authority", "high"));
        Assert.Equal("Status  Allowed", ApprovalCard.Field("Status", "allowed"));
    }

    [Fact]
    public void ActionLine_UsesPathNotRawJson()
    {
        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");

        var line = ApprovalCard.ActionLine(call);

        Assert.Equal("Write  src/App.cs", line);
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

        Assert.Equal("Write  src/App.cs", lines[0]);
        Assert.Contains("Status  Allowed", lines);
        Assert.Contains("Reason  Review", lines);
        Assert.Contains("Risk  Write", lines);
        Assert.Contains("Authority  Workspace", lines);
        Assert.Contains("Write workspace file", lines);
        Assert.Contains("Outcome  Allow", lines);
        Assert.Contains("Risk  Low", lines);
        Assert.Contains("Authority  High", lines);
        Assert.Contains("Adds the requested test.", lines);
        Assert.DoesNotContain(lines, line => line.Contains("Auth  ", StringComparison.Ordinal));
    }

    [Fact]
    public void PassWidget_RendersAlignedTitleCaseFields()
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

        var lines = WidgetPaint.Plain(
            ApprovalCard.PassWidget(call, classification, ApprovalPassReason.Review, review),
            72);
        var text = string.Join('\n', lines);

        Assert.Contains("Write  src/App.cs", text, StringComparison.Ordinal);
        Assert.Contains("Status", text, StringComparison.Ordinal);
        Assert.Contains("Allowed", text, StringComparison.Ordinal);
        Assert.Contains("Authority", text, StringComparison.Ordinal);
        Assert.Contains("Workspace", text, StringComparison.Ordinal);
        Assert.Contains("Outcome", text, StringComparison.Ordinal);
        Assert.Contains("Allow", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Auth  ", text, StringComparison.Ordinal);
        Assert.Contains("+x", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AskWidget_IncludesEditDiffPreview()
    {
        var classification = new ToolClassification(
            Risk.Write,
            Authority.Workspace,
            "Edit workspace file");
        var call = new ToolCall(
            "1",
            EditTool.ToolName,
            """{"path":"src/App.cs","old_string":"a","new_string":"b"}""");

        var text = string.Join(
            '\n',
            WidgetPaint.Plain(ApprovalCard.AskWidget(call, classification), 72));

        Assert.Contains("-a", text, StringComparison.Ordinal);
        Assert.Contains("+b", text, StringComparison.Ordinal);
        Assert.Contains("Y Once", text, StringComparison.Ordinal);
    }
}
