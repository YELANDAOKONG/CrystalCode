using CrystalCode.Approvals;

using Xunit;

namespace CrystalCode.Tests.Approvals;

public sealed class ApprovalModeTests
{
    [Fact]
    public void Parse_AcceptsReviewAndFull()
    {
        Assert.Equal(ApprovalMode.Review, ApprovalMode.Parse("review"));
        Assert.Equal(ApprovalMode.Full, ApprovalMode.Parse("full"));
        Assert.Equal(ApprovalMode.Full, ApprovalMode.Parse("fullauto"));
    }

    [Fact]
    public void Parse_RejectsAmbiguousAuto()
    {
        var exception = Assert.Throws<ArgumentException>(() => ApprovalMode.Parse("auto"));

        Assert.Contains("review", exception.Message, StringComparison.Ordinal);
        Assert.Contains("full", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_CyclesDefaultAutoEditReviewFull()
    {
        Assert.Equal(ApprovalMode.AutoEdit, ApprovalMode.Next(ApprovalMode.Default));
        Assert.Equal(ApprovalMode.Review, ApprovalMode.Next(ApprovalMode.AutoEdit));
        Assert.Equal(ApprovalMode.Full, ApprovalMode.Next(ApprovalMode.Review));
        Assert.Equal(ApprovalMode.Default, ApprovalMode.Next(ApprovalMode.Full));
        Assert.Equal(ApprovalMode.Default, ApprovalMode.Next(ApprovalMode.Plan));
    }
}
