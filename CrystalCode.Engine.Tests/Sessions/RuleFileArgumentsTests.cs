using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class RuleFileArgumentsTests
{
    [Fact]
    public void TryParse_EmptyArgumentOnlyDisplays()
    {
        var parsed = RuleFileArguments.TryParse("  ", out var change, out var error);

        Assert.True(parsed);
        Assert.Null(change);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParse_OneSideKeepsTheOther()
    {
        var parsed = RuleFileArguments.TryParse("project off", out var change, out var error);
        var applied = change!.Apply(RuleFileSettings.Default.WithGlobal(false));

        Assert.True(parsed);
        Assert.Equal(string.Empty, error);
        Assert.False(applied.Global);
        Assert.False(applied.Project);
    }

    [Fact]
    public void TryParse_RejectsAMissingSwitch()
    {
        var parsed = RuleFileArguments.TryParse("global", out var change, out var error);

        Assert.False(parsed);
        Assert.Null(change);
        Assert.Equal(RuleFileArguments.Usage, error);
    }
}
