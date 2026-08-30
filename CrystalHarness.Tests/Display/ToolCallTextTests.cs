using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ToolCallTextTests
{
    [Fact]
    public void Summary_PrefersCommandAndPath()
    {
        Assert.Equal("bash  uname -a", ToolCallText.Summary("bash", """{"command":"uname -a"}"""));
        Assert.Equal("write  src/App.cs", ToolCallText.Summary("write", """{"path":"src/App.cs","contents":"x"}"""));
    }

    [Fact]
    public void Summary_IgnoresPartialJson()
    {
        Assert.Equal("bash", ToolCallText.Summary("bash", """{"com"""));
    }
}
