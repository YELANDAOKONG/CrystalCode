using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class VerboseChangeArgumentsTests
{
    [Fact]
    public void TryParse_AllowsStatusQuery()
    {
        Assert.True(VerboseChangeArguments.TryParse(string.Empty, out var target, out var enabled, out var error));
        Assert.Null(target);
        Assert.Null(enabled);
        Assert.Empty(error);
    }

    [Fact]
    public void TryParse_ReadsToolsToggle()
    {
        Assert.True(VerboseChangeArguments.TryParse("tools", out var target, out _, out _));
        Assert.Equal(VerboseTarget.Tools, target);
    }

    [Fact]
    public void TryParse_ReadsCommandsOff()
    {
        Assert.True(VerboseChangeArguments.TryParse("commands off", out var target, out var enabled, out _));
        Assert.Equal(VerboseTarget.Commands, target);
        Assert.False(enabled);
    }

    [Fact]
    public void TryParse_ReadsThinkingOff()
    {
        Assert.True(VerboseChangeArguments.TryParse("thinking off", out var target, out var enabled, out _));
        Assert.Equal(VerboseTarget.Thinking, target);
        Assert.False(enabled);
    }

    [Fact]
    public void TryParse_ReadsApprovalsOff()
    {
        Assert.True(VerboseChangeArguments.TryParse("approvals off", out var target, out var enabled, out _));
        Assert.Equal(VerboseTarget.Approvals, target);
        Assert.False(enabled);
    }

    [Fact]
    public void TryParse_RejectsUnknownTarget()
    {
        Assert.False(VerboseChangeArguments.TryParse("all on", out _, out _, out var error));
        Assert.Contains("tools", error, StringComparison.OrdinalIgnoreCase);
    }
}
