using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PromptSelectionTextTests
{
    [Fact]
    public void Format_DistinguishesSelectionFromEffectiveSources()
    {
        var resolution = new PromptResolution(
            new PromptSet("work", "plan", "review", string.Empty),
            "concise",
            ["concise", "strict-review"],
            PromptSource.HomeOverride,
            PromptSource.PromptSet,
            PromptSource.ProjectOverride,
            [],
            [],
            [
                new PromptSetEntry("concise", "Concise", "Shorter replies", true, true),
                new PromptSetEntry("strict-review", "strict-review", string.Empty, false, false)
            ]);

        var text = PromptSelectionText.Format(resolution);

        Assert.Contains("Prompt Set: concise", text, StringComparison.Ordinal);
        Assert.Contains("* concise  Concise  Shorter replies", text, StringComparison.Ordinal);
        Assert.Contains("strict-review", text, StringComparison.Ordinal);
        Assert.Contains("Work    Home Override", text, StringComparison.Ordinal);
        Assert.Contains("Plan    Prompt Set concise", text, StringComparison.Ordinal);
        Assert.Contains("Review  Project Override", text, StringComparison.Ordinal);
    }
}
