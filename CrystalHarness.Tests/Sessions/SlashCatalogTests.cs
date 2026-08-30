using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

public sealed class SlashCatalogTests
{
    [Theory]
    [InlineData("/new", SessionVerb.Clear)]
    [InlineData("/clear", SessionVerb.Clear)]
    [InlineData("/continue", SessionVerb.Resume)]
    [InlineData("/sessions", SessionVerb.Resume)]
    [InlineData("/h", SessionVerb.Help)]
    [InlineData("/exit", SessionVerb.Quit)]
    public void TryParse_MapsAliases(string input, SessionVerb verb)
    {
        var parsed = SessionCommand.TryParse(input, out var command);

        Assert.True(parsed);
        Assert.Equal(verb, command.Verb);
    }
}
