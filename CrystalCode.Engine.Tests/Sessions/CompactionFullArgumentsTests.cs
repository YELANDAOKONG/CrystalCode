using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CompactionFullArgumentsTests
{
    [Theory]
    [InlineData("full", true)]
    [InlineData("  FULL  ", true)]
    [InlineData("full on", true)]
    [InlineData("full\tx", true)]
    [InlineData("fuller", false)]
    [InlineData("model", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsFullCommand_MatchesTheFullWord(string? argument, bool expected)
    {
        Assert.Equal(expected, CompactionFullArguments.IsFullCommand(argument));
    }

    [Fact]
    public void TryParse_AcceptsOnlyTheBareWord()
    {
        Assert.True(CompactionFullArguments.TryParse("full", out var bare));
        Assert.Equal(string.Empty, bare);

        Assert.False(CompactionFullArguments.TryParse("full on", out var error));
        Assert.Equal("Full compaction takes no argument.", error);
    }
}
