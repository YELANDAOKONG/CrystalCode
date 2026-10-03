using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ApprovalModelArgumentsTests
{
    [Theory]
    [InlineData("model", true)]
    [InlineData("MODEL on", true)]
    [InlineData("model\toff", true)]
    [InlineData("review", false)]
    [InlineData("modelx", false)]
    [InlineData("", false)]
    public void IsModelCommand_RecognizesOnlyTheModelSubcommand(string argument, bool expected)
    {
        Assert.Equal(expected, ApprovalModelArguments.IsModelCommand(argument));
    }

    [Fact]
    public void TryParse_ReadsShowSwitchAndSelection()
    {
        Assert.True(ApprovalModelArguments.TryParse("model", out var show, out var showError));
        Assert.Equal(string.Empty, showError);
        Assert.True(show.Show);

        Assert.True(ApprovalModelArguments.TryParse("model on", out var on, out _));
        Assert.True(on.Enabled);
        Assert.False(on.Show);

        Assert.True(ApprovalModelArguments.TryParse("MODEL off", out var off, out _));
        Assert.False(off.Enabled);

        Assert.True(ApprovalModelArguments.TryParse(
            "model openai gpt-5.6-sol",
            out var selection,
            out _));
        Assert.Equal("openai gpt-5.6-sol", selection.Selection);
        Assert.Null(selection.Enabled);
    }

    [Fact]
    public void TryParse_RejectsASwitchWithTrailingText()
    {
        var parsed = ApprovalModelArguments.TryParse("model on extra", out _, out var error);

        Assert.False(parsed);
        Assert.Equal("Approval model is on or off, or a provider and model.", error);
    }
}
