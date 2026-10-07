using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PromptAttachmentTextTests
{
    [Fact]
    public void Format_NumbersEnabledRowsAndLeavesDisabledRowsUnnumbered()
    {
        var resolution = Resolution(
        [
            new PromptAttachmentEntry("beta", PromptAttachmentSource.Workspace, Enabled: true),
            new PromptAttachmentEntry("gone", null, Enabled: true),
            new PromptAttachmentEntry("alpha", PromptAttachmentSource.Home, Enabled: false)
        ]);

        var text = PromptAttachmentText.Format(resolution);

        Assert.Contains("* 1  beta  Workspace", text, StringComparison.Ordinal);
        Assert.Contains("* 2  gone  Not found", text, StringComparison.Ordinal);
        Assert.Contains("alpha  Home", text, StringComparison.Ordinal);
        Assert.DoesNotContain("* 3", text, StringComparison.Ordinal);
        Assert.Equal("beta", PromptAttachmentText.Status(resolution));
    }

    [Fact]
    public void Format_ShowsNoneWhenNothingIsDiscovered()
    {
        var text = PromptAttachmentText.Format(Resolution([]));

        Assert.Contains("(none)", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, PromptAttachmentText.Status(Resolution([])));
    }

    private static PromptResolution Resolution(IReadOnlyList<PromptAttachmentEntry> attachments) =>
        new(
            new PromptSet("work", "plan", "review", string.Empty),
            PromptSetNames.Default,
            [],
            PromptSource.BuiltIn,
            PromptSource.BuiltIn,
            PromptSource.BuiltIn,
            [],
            attachments);
}
