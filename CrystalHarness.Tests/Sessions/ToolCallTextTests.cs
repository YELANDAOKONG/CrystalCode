using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

public sealed class ToolCallTextTests
{
    [Fact]
    public void Summary_PrefersCommandAndPath()
    {
        Assert.Equal("Bash  uname -a", ToolCallText.Summary("bash", """{"command":"uname -a"}"""));
        Assert.Equal("Write  src/App.cs", ToolCallText.Summary("write", """{"path":"src/App.cs","contents":"x"}"""));
    }

    [Fact]
    public void Summary_IgnoresPartialJson()
    {
        Assert.Equal("Bash", ToolCallText.Summary("bash", """{"com"""));
    }
}
