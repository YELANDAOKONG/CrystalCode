using CrystalCode.Engine.Prompts;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class CompactionPromptTests
{
    [Fact]
    public void OutputTemplateText_ContainsStableRules()
    {
        var text = CompactionPrompt.OutputTemplateText;

        Assert.Contains("Keep every section, even when empty.", text, StringComparison.Ordinal);
        Assert.Contains("Preserve exact file paths", text, StringComparison.Ordinal);
        Assert.Contains("Record what was verified and how.", text, StringComparison.Ordinal);
        Assert.Contains(
            "Keep the user's explicit requests and constraints exact in scope",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Record errors and dead ends and how they were resolved",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Base Next Move only on the user's most recent request",
            text,
            StringComparison.Ordinal);
        Assert.Contains("Do not mention the summary process", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUserContext_WithoutPreviousSummary_TasksAFreshSummary()
    {
        var context = CompactionPrompt.CreateUserContext("turn one", "No todos.", previousSummary: null);

        Assert.Equal("turn one", context.Conversation);
        Assert.Equal(string.Empty, context.PriorSummarySection);
        Assert.Equal(string.Empty, context.TodosSection);
        Assert.Contains("Create a new anchored summary", context.SummaryTask, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUserContext_WithPreviousSummary_FoldsItAndKeepsTodos()
    {
        var context = CompactionPrompt.CreateUserContext("turn one", "1. Ship the fix.", "Old summary body.");

        Assert.Contains("<prior-summary>", context.PriorSummarySection, StringComparison.Ordinal);
        Assert.Contains("Old summary body.", context.PriorSummarySection, StringComparison.Ordinal);
        Assert.Contains("conversation wins", context.PriorSummarySection, StringComparison.Ordinal);
        Assert.Equal(string.Empty, context.SummaryTask);
        Assert.Contains("Carry each unfinished one", context.TodosSection, StringComparison.Ordinal);
    }
}
