using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CompactionSummaryArgumentsTests
{
    [Theory]
    [InlineData("summary", true, false)]
    [InlineData("SUMMARY on", true, false)]
    [InlineData("auto", false, true)]
    [InlineData("automatic off", false, true)]
    [InlineData("model", false, false)]
    [InlineData("automatics", false, false)]
    public void IsCommand_MatchesSummaryAndAuto(string argument, bool summary, bool automatic)
    {
        Assert.Equal(summary, CompactionSummaryArguments.IsSummaryCommand(argument));
        Assert.Equal(automatic, CompactionSummaryArguments.IsAutomaticCommand(argument));
    }

    [Fact]
    public void TryParse_ReadsAToggleOrAnExplicitSwitch()
    {
        Assert.True(CompactionSummaryArguments.TryParse("summary", false, out var toggle, out _));
        Assert.False(toggle.Automatic);
        Assert.Null(toggle.Enabled);

        Assert.True(CompactionSummaryArguments.TryParse("summary OFF", false, out var off, out _));
        Assert.False(off.Enabled);

        Assert.True(CompactionSummaryArguments.TryParse("automatic on", true, out var on, out _));
        Assert.True(on.Automatic);
        Assert.True(on.Enabled);

        Assert.False(CompactionSummaryArguments.TryParse("auto later", true, out _, out var error));
        Assert.Equal("Automatic compaction summary is on or off.", error);
        Assert.False(CompactionSummaryArguments.TryParse("summary on extra", false, out _, out var manual));
        Assert.Equal("Compaction summary is on or off.", manual);
    }
}
