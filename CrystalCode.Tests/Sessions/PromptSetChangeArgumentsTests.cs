using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class PromptSetChangeArgumentsTests
{
    [Fact]
    public void TryParseName_AcceptsSingleName()
    {
        var parsed = PromptSetChangeArguments.TryParseName(["concise"], out var name, out var error);

        Assert.True(parsed);
        Assert.Empty(error);
        Assert.Equal("concise", name);
    }

    [Fact]
    public void TryParseName_RejectsMultipleNames()
    {
        var parsed = PromptSetChangeArguments.TryParseName(
            ["concise", "extra"],
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Equal("Prompt set accepts at most one name.", error);
    }
}
