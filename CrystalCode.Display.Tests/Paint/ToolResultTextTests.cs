using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

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

    [Fact]
    public void CompactCommandBody_ShowsHiddenCountAndLastLine()
    {
        var text = "exit 0\nline1\nline2\nsummary";
        var compact = ToolResultText.CompactCommandBody(text);

        Assert.Contains("2 output lines hidden", compact, StringComparison.Ordinal);
        Assert.Contains(ToolResultText.CommandExpandHint, compact, StringComparison.Ordinal);
        Assert.Contains("summary", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("line1", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactCommandBody_KeepsSingleLineWithoutHint()
    {
        var compact = ToolResultText.CompactCommandBody("exit 0\nonly line");

        Assert.Equal("only line", compact);
    }
}
