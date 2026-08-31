using CrystalCode.Prompts;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class PlanPromptTests
{
    [Fact]
    public void Text_NamesCrystalCodeWithoutForbiddingEdits()
    {
        Assert.Contains("You are Crystal Code", PlanPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("todowrite", PlanPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("question", PlanPrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not edit", PlanPrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not run", PlanPrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("emoji", PlanPrompt.Text, StringComparison.OrdinalIgnoreCase);
    }
}
