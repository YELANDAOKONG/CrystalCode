using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CompactionModelArgumentsTests
{
    [Theory]
    [InlineData("model", true)]
    [InlineData("MODEL on", true)]
    [InlineData("model\toff", true)]
    [InlineData("thinking", false)]
    [InlineData("modelx", false)]
    [InlineData("", false)]
    public void IsModelCommand_RecognizesOnlyTheModelSubcommand(string argument, bool expected)
    {
        Assert.Equal(expected, CompactionModelArguments.IsModelCommand(argument));
    }

    [Fact]
    public void TryParse_ReadsShowSwitchAndSelection()
    {
        Assert.True(CompactionModelArguments.TryParse("model", out var show, out _));
        Assert.True(show.Show);

        Assert.True(CompactionModelArguments.TryParse("model off", out var off, out _));
        Assert.False(off.Enabled);

        Assert.True(CompactionModelArguments.TryParse("model scripted deep", out var selected, out _));
        Assert.Equal("scripted deep", selected.Selection);

        Assert.False(CompactionModelArguments.TryParse("model on extra", out _, out var error));
        Assert.Equal("Compaction model is on or off, or a provider and model.", error);
    }
}
