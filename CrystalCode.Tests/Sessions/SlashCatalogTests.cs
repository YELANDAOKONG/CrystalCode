using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class SlashCatalogTests
{
    [Fact]
    public void Status_OffersFullCompletion()
    {
        var status = SlashCatalog.BuiltIn.Single(spec => spec.Verb == SessionVerb.Status);

        Assert.Equal(["full"], status.Arguments);
    }

    [Theory]
    [InlineData("/new", SessionVerb.Clear)]
    [InlineData("/clear", SessionVerb.Clear)]
    [InlineData("/continue", SessionVerb.Resume)]
    [InlineData("/fork", SessionVerb.Fork)]
    [InlineData("/sessions", SessionVerb.Sessions)]
    [InlineData("/h", SessionVerb.Help)]
    [InlineData("/think", SessionVerb.Thinking)]
    [InlineData("/exit", SessionVerb.Quit)]
    [InlineData("/compact", SessionVerb.Compact)]
    [InlineData("/summarize", SessionVerb.Compact)]
    [InlineData("/model", SessionVerb.Model)]
    [InlineData("/promptset", SessionVerb.PromptSet)]
    [InlineData("/prompts", SessionVerb.PromptSet)]
    [InlineData("/tokens", SessionVerb.Tokens)]
    [InlineData("/todos", SessionVerb.Todos)]
    [InlineData("/todo", SessionVerb.Todos)]
    [InlineData("/tools", SessionVerb.Tools)]
    public void TryParse_MapsAliases(string input, SessionVerb verb)
    {
        var parsed = SessionCommand.TryParse(input, out var command);

        Assert.True(parsed);
        Assert.Equal(verb, command.Verb);
    }
}
