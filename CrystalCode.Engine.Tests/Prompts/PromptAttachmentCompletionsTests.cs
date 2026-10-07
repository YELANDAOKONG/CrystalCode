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
                new PromptAttachmentEntry("beta", PromptAttachmentSource.Home, Enabled: true),
                new PromptAttachmentEntry("gone", null, Enabled: true),
                new PromptAttachmentEntry("alpha", PromptAttachmentSource.Workspace, Enabled: false)
            ]);

        var options = PromptAttachmentCompletions.For(resolution);

        Assert.Equal(["enable", "disable", "up", "down"], options.Select(option => option.Name));
        Assert.Equal(["beta", "alpha"], options[0].ArgumentOptions.Select(option => option.Name));
        Assert.Equal(["beta", "gone"], options[1].ArgumentOptions.Select(option => option.Name));
        Assert.Equal(["beta", "gone"], options[2].ArgumentOptions.Select(option => option.Name));
    }
}
