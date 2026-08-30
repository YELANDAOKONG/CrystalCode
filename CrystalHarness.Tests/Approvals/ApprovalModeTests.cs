using CrystalHarness.Approvals;

using Xunit;

namespace CrystalHarness.Tests.Approvals;

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
}
