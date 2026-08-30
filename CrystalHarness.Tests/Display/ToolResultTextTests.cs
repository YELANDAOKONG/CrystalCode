using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ToolResultTextTests
{
    [Fact]
    public void Summary_SkipsSuccessfulExitHeader()
    {
        var line = ToolResultText.Summary("exit 0\nLinux 7.0.0-30-generic");

        Assert.Equal("Linux 7.0.0-30-generic", line);
    }

    [Fact]
    public void Summary_KeepsExitWhenThatIsAll()
    {
        Assert.Equal("exit 1", ToolResultText.Summary("exit 1\n"));
    }
}
