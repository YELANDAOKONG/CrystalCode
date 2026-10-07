using CrystalCode.Engine.Prompts;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class PromptAttachmentCompletionsTests
{
    [Fact]
    public void For_OffersDiscoveredNamesToEnableAndEnabledNamesToReorder()
    {
        var resolution = new PromptResolution(
            new PromptSet("work", "plan", "review", string.Empty),
            PromptSetNames.Default,
            [],
            PromptSource.BuiltIn,
            PromptSource.BuiltIn,
            PromptSource.BuiltIn,
            [],
            [
                new PromptAttachmentEntry("beta", "beta", string.Empty, PromptAttachmentSource.Home, true, true, 0),
                new PromptAttachmentEntry("alpha", "alpha", string.Empty, PromptAttachmentSource.Workspace, false, false, null)
            ],
            []);

        var options = PromptAttachmentCompletions.For(resolution);

        Assert.Equal(["enable", "disable", "up", "down"], options.Select(option => option.Name));
        Assert.Equal(["beta", "alpha"], options[0].ArgumentOptions.Select(option => option.Name));
        Assert.Equal(["beta"], options[1].ArgumentOptions.Select(option => option.Name));
        Assert.Equal(["beta"], options[2].ArgumentOptions.Select(option => option.Name));
    }
}
