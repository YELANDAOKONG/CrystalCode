using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

public sealed class DisplayCaseTests
{
    [Fact]
    public void Token_TitleCasesKnownModes()
    {
        Assert.Equal("Review", DisplayCase.Token("review"));
        Assert.Equal("Read", DisplayCase.Token("read"));
        Assert.Equal("TodoWrite", DisplayCase.Token("todowrite"));
        Assert.Equal("TodoRead", DisplayCase.Token("todoread"));
        Assert.Equal("Edit", DisplayCase.Token("edit"));
        Assert.Equal("Audit", DisplayCase.Token("audit"));
        Assert.Equal("Outside Workspace", DisplayCase.Token("outside_workspace"));
        Assert.Equal("Interrupted", DisplayCase.Token("interrupted"));
    }
}
