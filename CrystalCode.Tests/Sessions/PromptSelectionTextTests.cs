using CrystalCode.Prompts;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

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
            []);

        var text = PromptSelectionText.Format(resolution);

        Assert.Contains("Prompt Set: concise", text, StringComparison.Ordinal);
        Assert.Contains("* concise", text, StringComparison.Ordinal);
        Assert.Contains("Work    Home Override", text, StringComparison.Ordinal);
        Assert.Contains("Plan    Prompt Set concise", text, StringComparison.Ordinal);
        Assert.Contains("Review  Project Override", text, StringComparison.Ordinal);
    }
}
