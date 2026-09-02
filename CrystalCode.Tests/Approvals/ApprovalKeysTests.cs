using CrystalCode.Approvals;

using Xunit;

namespace CrystalCode.Tests.Approvals;

public sealed class ApprovalKeysTests
{
    [Fact]
    public void TryMap_UsesLetterKeys()
    {
        Assert.True(ApprovalKeys.TryMap(ConsoleKey.Y, out var once));
        Assert.Equal(ApprovalChoice.AllowOnce, once);
        Assert.True(ApprovalKeys.TryMap(ConsoleKey.S, out var session));
        Assert.Equal(ApprovalChoice.AllowSession, session);
        Assert.True(ApprovalKeys.TryMap(ConsoleKey.A, out var always));
        Assert.Equal(ApprovalChoice.AllowPersistent, always);
        Assert.True(ApprovalKeys.TryMap(ConsoleKey.N, out var deny));
        Assert.Equal(ApprovalChoice.Deny, deny);
    }

    [Fact]
    public void For_TitleCasesApprovalMode()
    {
        Assert.Equal("Review", ApprovalLabel.For(ApprovalMode.Review));
        Assert.Equal("Audit", ApprovalLabel.For(ApprovalMode.Audit));
        Assert.Equal("Edit", ApprovalLabel.For(ApprovalMode.Edit));
    }
}
