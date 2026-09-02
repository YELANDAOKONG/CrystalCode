using CrystalCode.Prompts;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class PromptSetCompletionsTests
{
    [Fact]
    public void For_ListsDefaultBeforeDiscoveredSets()
    {
        var resolution = new PromptResolution(
            new PromptSet("work", "plan", "review", string.Empty),
            "concise",
            ["concise", "strict-review"],
            PromptSource.PromptSet,
            PromptSource.BuiltIn,
            PromptSource.BuiltIn,
            []);

        var options = PromptSetCompletions.For(resolution);

        Assert.Equal(
            ["default", "export", "concise", "strict-review"],
            options.Select(option => option.Name));
        Assert.Equal([".", "./prompts"], options[1].ArgumentOptions.Select(option => option.Name));
    }
}
