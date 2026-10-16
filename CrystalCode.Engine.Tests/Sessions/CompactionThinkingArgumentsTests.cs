using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class CompactionThinkingArgumentsTests
{
    [Theory]
    [InlineData("thinking", true)]
    [InlineData("THINKING high", true)]
    [InlineData("thinking\thigh", true)]
    [InlineData("model", false)]
    [InlineData("thinkingx", false)]
    [InlineData("", false)]
    public void IsThinkingCommand_RecognizesOnlyTheThinkingSubcommand(
        string argument,
        bool expected)
    {
        Assert.Equal(expected, CompactionThinkingArguments.IsThinkingCommand(argument));
    }

    [Fact]
    public void Parse_ReadsTheOptionalEffort()
    {
        Assert.Null(CompactionThinkingArguments.Parse("thinking").Effort);
        Assert.Equal("high", CompactionThinkingArguments.Parse("THINKING high").Effort);
        Assert.Equal("maximum", CompactionThinkingArguments.Parse("thinking  maximum ").Effort);
    }
}
