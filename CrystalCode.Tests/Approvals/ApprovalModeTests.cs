using CrystalCode.Approvals;

using Xunit;

namespace CrystalCode.Tests.Approvals;

public sealed class ApprovalModeTests
{
    [Fact]
    public void Parse_AcceptsEditReviewAuditAndFull()
    {
        Assert.Equal(ApprovalMode.Edit, ApprovalMode.Parse("edit"));
        Assert.Equal(ApprovalMode.Review, ApprovalMode.Parse("review"));
        Assert.Equal(ApprovalMode.Audit, ApprovalMode.Parse("audit"));
        Assert.Equal(ApprovalMode.Full, ApprovalMode.Parse("full"));
        Assert.Equal(ApprovalMode.Full, ApprovalMode.Parse("fullauto"));
    }

    [Fact]
    public void Parse_AcceptsLegacyAutoEditAndFullReview()
    {
        Assert.Equal(ApprovalMode.Edit, ApprovalMode.Parse("autoedit"));
        Assert.Equal(ApprovalMode.Audit, ApprovalMode.Parse("fullreview"));
        Assert.Equal(ApprovalMode.Audit, ApprovalMode.Parse("full-review"));
    }

    [Fact]
    public void Parse_RejectsAmbiguousAuto()
    {
        var exception = Assert.Throws<ArgumentException>(() => ApprovalMode.Parse("auto"));

        Assert.Contains("review", exception.Message, StringComparison.Ordinal);
        Assert.Contains("full", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_CyclesDefaultEditReviewAuditFull()
    {
        Assert.Equal(ApprovalMode.Edit, ApprovalMode.Next(ApprovalMode.Default));
        Assert.Equal(ApprovalMode.Review, ApprovalMode.Next(ApprovalMode.Edit));
        Assert.Equal(ApprovalMode.Audit, ApprovalMode.Next(ApprovalMode.Review));
        Assert.Equal(ApprovalMode.Full, ApprovalMode.Next(ApprovalMode.Audit));
        Assert.Equal(ApprovalMode.Default, ApprovalMode.Next(ApprovalMode.Full));
        Assert.Equal(ApprovalMode.Default, ApprovalMode.Next(ApprovalMode.Plan));
    }
}
