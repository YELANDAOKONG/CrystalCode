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
    [InlineData("/fork", SessionVerb.Fork)]
    [InlineData("/fork abc123", SessionVerb.Fork)]
    [InlineData("/sessions", SessionVerb.Sessions)]
    [InlineData("/sessions all", SessionVerb.Sessions)]
    [InlineData("/compact", SessionVerb.Compact)]
    [InlineData("/model", SessionVerb.Model)]
    [InlineData("/promptset concise", SessionVerb.PromptSet)]
    [InlineData("/prompts", SessionVerb.PromptSet)]
    [InlineData("/tokens", SessionVerb.Tokens)]
    [InlineData("/export", SessionVerb.Export)]
    [InlineData("/export markdown", SessionVerb.Export)]
    [InlineData("/export json --system", SessionVerb.Export)]
    [InlineData("/todos", SessionVerb.Todos)]
    [InlineData("/todo", SessionVerb.Todos)]
    public void TryParse_RecognizesSlashVerbs(string input, SessionVerb verb)
    {
        var parsed = SessionCommand.TryParse(input, out var command);

        Assert.True(parsed);
        Assert.Equal(verb, command.Verb);
    }

    [Fact]
    public void TryParse_ReadsExportArgument()
    {
        SessionCommand.TryParse("/export markdown ./out.md --system", out var command);

        Assert.Equal(SessionVerb.Export, command.Verb);
        Assert.Equal("markdown ./out.md --system", command.Argument);
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
    public void TryParse_ReadsForkId()
    {
        SessionCommand.TryParse("/fork abc123", out var command);

        Assert.Equal(SessionVerb.Fork, command.Verb);
        Assert.Equal("abc123", command.Argument);
    }

    [Fact]
    public void TryParse_IgnoresPlainText()
    {
        Assert.False(SessionCommand.TryParse("fix the test", out _));
    }
}
