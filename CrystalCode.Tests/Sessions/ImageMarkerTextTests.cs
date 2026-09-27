using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ImageMarkerTextTests
{
    [Fact]
    public void TagLegacy_UpgradesSavedImageReferencesAndLeavesUnknownText()
    {
        var items = ImageMarkerText.TagLegacy(
            [
                new ChatMessage(ChatRole.User, "saved [Image #1] typed [Image #2]"),
                new ToolResult("call_1", "result [Image #1]")
            ],
            new HashSet<int> { 1 });

        var user = Assert.IsType<ChatMessage>(items[0]);
        Assert.Equal("saved \u2063[Image #1] typed [Image #2]", user.Text);
        Assert.Equal("saved [Image #1] typed [Image #2]",
            ImageMarkerText.Display(user.Text));
        Assert.Equal("result \u2063[Image #1]",
            Assert.IsType<ToolResult>(items[1]).Text);
    }
}
