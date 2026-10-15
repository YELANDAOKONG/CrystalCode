using CrystalCode.Engine.Approvals;

using Xunit;
using CrystalCode.Terminal.Approvals;

namespace CrystalCode.Tests.Terminal.Approvals;

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

    [Theory]
    [InlineData(ConsoleKey.S)]
    [InlineData(ConsoleKey.A)]
    [InlineData(ConsoleKey.D2)]
    [InlineData(ConsoleKey.D3)]
    public void TryMap_OnceOnlyApproval_IgnoresReusableGrantKeys(ConsoleKey key)
    {
        Assert.False(ApprovalKeys.TryMap(key, out _, allowReusableGrant: false));
        Assert.True(ApprovalKeys.TryMap(ConsoleKey.Y, out var once, allowReusableGrant: false));
        Assert.Equal(ApprovalChoice.AllowOnce, once);
        Assert.True(ApprovalKeys.TryMap(ConsoleKey.N, out var deny, allowReusableGrant: false));
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
