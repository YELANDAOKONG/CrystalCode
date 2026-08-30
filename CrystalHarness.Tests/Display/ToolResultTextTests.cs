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

    [Fact]
    public void Body_KeepsOutputLinesAfterExit()
    {
        var body = ToolResultText.Body("exit 0\nLinux 7.0.0-30-generic\nID=xiyueos");

        Assert.Contains("Linux 7.0.0-30-generic", body, StringComparison.Ordinal);
        Assert.Contains("ID=xiyueos", body, StringComparison.Ordinal);
        Assert.DoesNotContain("exit 0", body, StringComparison.Ordinal);
    }
}
