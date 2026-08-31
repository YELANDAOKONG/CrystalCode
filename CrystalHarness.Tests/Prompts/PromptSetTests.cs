using CrystalHarness.Prompts;

using Xunit;

namespace CrystalHarness.Tests.Prompts;

public sealed class PromptSetTests
{
    [Fact]
    public void ComposeWork_InsertsEnvironmentBeforeInstructions()
    {
        var set = new PromptSet("work body", "plan body", "review body", "prefer tests");

        var text = set.ComposeWork("<env>\n  Workspace: /tmp/demo\n</env>");

        Assert.StartsWith("work body", text, StringComparison.Ordinal);
        var env = text.IndexOf("<env>", StringComparison.Ordinal);
        var instructions = text.IndexOf("## Workspace instructions", StringComparison.Ordinal);
        Assert.True(env > 0);
        Assert.True(instructions > env);
        Assert.Contains("prefer tests", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<env>", set.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("prefer tests", set.WorkSystem, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposePlan_UsesPlanBody()
    {
        var set = new PromptSet("work body", "plan body", "review body", string.Empty);

        var text = set.ComposePlan("<env>\n  Workspace: /tmp/demo\n</env>");

        Assert.StartsWith("plan body", text, StringComparison.Ordinal);
        Assert.Contains("<env>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace instructions", text, StringComparison.Ordinal);
    }
}
