using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class SessionCommandTests
{
    [Theory]
    [InlineData("/help", SessionVerb.Help)]
    [InlineData("/plan", SessionVerb.Plan)]
    [InlineData("/approval review", SessionVerb.Approval)]
    [InlineData("/thinking high", SessionVerb.Thinking)]
    [InlineData("/think off", SessionVerb.Thinking)]
    [InlineData("/resume", SessionVerb.Resume)]
    [InlineData("/quit", SessionVerb.Quit)]
    [InlineData("/new", SessionVerb.Clear)]
    [InlineData("/continue", SessionVerb.Resume)]
    [InlineData("/compact", SessionVerb.Compact)]
    [InlineData("/model", SessionVerb.Model)]
    public void TryParse_RecognizesSlashVerbs(string input, SessionVerb verb)
    {
        var parsed = SessionCommand.TryParse(input, out var command);

        Assert.True(parsed);
        Assert.Equal(verb, command.Verb);
    }

    [Fact]
    public void TryParse_ReadsApprovalArgument()
    {
        SessionCommand.TryParse("/approval full", out var command);

        Assert.Equal(SessionVerb.Approval, command.Verb);
        Assert.Equal("full", command.Argument);
    }

    [Fact]
    public void TryParse_ReadsModelProviderAndSlashedId()
    {
        SessionCommand.TryParse(
            "/model openrouter anthropic/claude-sonnet-4",
            out var command);

        Assert.Equal(SessionVerb.Model, command.Verb);
        Assert.Equal("openrouter anthropic/claude-sonnet-4", command.Argument);
    }

    [Fact]
    public void TryParse_IgnoresPlainText()
    {
        Assert.False(SessionCommand.TryParse("fix the test", out _));
    }
}
