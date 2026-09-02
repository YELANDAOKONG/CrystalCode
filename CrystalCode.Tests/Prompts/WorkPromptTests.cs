using CrystalCode.Prompts;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class WorkPromptTests
{
    [Fact]
    public void Text_NamesCrystalCodeAndAsksWhenUncertain()
    {
        Assert.Contains("You are {{product_name}}", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("{{env}}", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("{{skills}}", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("{{instructions_section}}", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("todowrite", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("todoread", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("When you are uncertain", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("smallest useful set", WorkPrompt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("emoji", WorkPrompt.Text, StringComparison.OrdinalIgnoreCase);
    }
}
