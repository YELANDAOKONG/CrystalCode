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
        Assert.Equal("AutoEdit", DisplayCase.Token("autoedit"));
        Assert.Equal("Outside Workspace", DisplayCase.Token("outside_workspace"));
    }
}
