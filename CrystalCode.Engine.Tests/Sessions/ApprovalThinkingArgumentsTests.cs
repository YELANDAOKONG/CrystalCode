using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ApprovalThinkingArgumentsTests
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
        Assert.Equal(expected, ApprovalThinkingArguments.IsThinkingCommand(argument));
    }

    [Fact]
    public void Parse_ReadsTheOptionalEffort()
    {
        Assert.Null(ApprovalThinkingArguments.Parse("thinking").Effort);
        Assert.Equal("high", ApprovalThinkingArguments.Parse("THINKING high").Effort);
        Assert.Equal("maximum", ApprovalThinkingArguments.Parse("thinking  maximum ").Effort);
    }
}
