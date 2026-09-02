using CrystalCode.Prompts;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class PromptSetTests
{
    [Fact]
    public void ComposeWork_InsertsEnvironmentBeforeInstructions()
    {
        var set = new PromptSet(WorkPrompt.Text, "plan body", "review body", "prefer tests");
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "work",
            string.Empty,
            "prefer tests",
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var text = set.ComposeWork(context);

        Assert.StartsWith("You are Crystal Code", text, StringComparison.Ordinal);
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
        var set = new PromptSet("work body", PlanPrompt.Text, "review body", string.Empty);
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "plan",
            string.Empty,
            string.Empty,
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var text = set.ComposePlan(context);

        Assert.StartsWith("You are Crystal Code, planning", text, StringComparison.Ordinal);
        Assert.Contains("<env>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace instructions", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeWork_InsertsSkillGuidanceBetweenEnvironmentAndInstructions()
    {
        var set = new PromptSet(WorkPrompt.Text, "plan body", "review body", "prefer tests");
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "work",
            "Skills provide specialized instructions.",
            "prefer tests",
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var text = set.ComposeWork(context);

        var env = text.IndexOf("<env>", StringComparison.Ordinal);
        var skills = text.IndexOf("Skills provide", StringComparison.Ordinal);
        var instructions = text.IndexOf("## Workspace instructions", StringComparison.Ordinal);
        Assert.True(env > 0);
        Assert.True(skills > env);
        Assert.True(instructions > skills);
        Assert.DoesNotContain("Skills provide", set.WorkSystem, StringComparison.Ordinal);
    }
}
