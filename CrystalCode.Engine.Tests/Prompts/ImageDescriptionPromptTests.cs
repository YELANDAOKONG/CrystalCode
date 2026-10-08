using CrystalCode.Engine.Prompts;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class ImageDescriptionPromptTests
{
    [Fact]
    public void SystemText_TellsTheVisionModelHowATextAgentWillUseTheReply()
    {
        Assert.Contains("{{product_name}}", ImageDescriptionPrompt.SystemText, StringComparison.Ordinal);
        Assert.Contains("{{env}}", ImageDescriptionPrompt.SystemText, StringComparison.Ordinal);
        Assert.Contains(
            "cannot see the image",
            ImageDescriptionPrompt.SystemText,
            StringComparison.Ordinal);
        Assert.Contains(
            "Do not invent",
            ImageDescriptionPrompt.SystemText,
            StringComparison.Ordinal);
        Assert.Contains(
            "When the user message includes a question",
            ImageDescriptionPrompt.SystemText,
            StringComparison.Ordinal);

        var composed = ImageDescriptionPrompt.ComposeSystem(
            PromptContext.InstructionsOnly(string.Empty));

        Assert.Contains("Crystal Code", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("{{product_name}}", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("{{env}}", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void UserText_AsksForADescriptionOrCarriesTheQuestion()
    {
        Assert.Equal(
            ImageDescriptionPrompt.DescribeRequest,
            ImageDescriptionPrompt.UserText(null));
        Assert.Equal(
            ImageDescriptionPrompt.DescribeRequest,
            ImageDescriptionPrompt.UserText("  "));
        Assert.Equal(
            "Question: what error is shown",
            ImageDescriptionPrompt.UserText("  what error is shown  "));
        Assert.Contains(
            "Question: the color",
            ImageDescriptionPrompt.UserText("the color", "Focus.\n{{question}}"),
            StringComparison.Ordinal);
    }
}
