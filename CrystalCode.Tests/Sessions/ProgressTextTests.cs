using CrystalCode.Sessions;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ProgressTextTests
{
    [Fact]
    public void Running_UsesCommandCaptionForBash()
    {
        Assert.Equal("Running Command", ProgressText.Running(BashTool.ToolName));
    }

    [Theory]
    [InlineData(ReadTool.ToolName, "Reading")]
    [InlineData(EditTool.ToolName, "Editing")]
    [InlineData(WriteTool.ToolName, "Writing File")]
    [InlineData(GrepTool.ToolName, "Searching")]
    public void Running_MapsKnownTools(string name, string expected)
    {
        Assert.Equal(expected, ProgressText.Running(name));
    }

    [Fact]
    public void Calling_TitleCasesTheTool()
    {
        Assert.Equal("Calling TodoWrite", ProgressText.Calling("todowrite"));
    }

    [Fact]
    public void Retrying_UsesElapsedCaption()
    {
        Assert.Equal(
            "Retrying In 8s (Attempt 2)",
            ProgressText.Retrying(2, TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public void Running_UnknownToolKeepsToken()
    {
        Assert.Equal("Running: Custom Tool", ProgressText.Running("custom_tool"));
    }
}
