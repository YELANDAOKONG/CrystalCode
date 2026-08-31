using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ToolCallTextTests
{
    [Fact]
    public void Summary_PrefersCommandAndPath()
    {
        Assert.Equal("Bash  uname -a", ToolCallText.Summary("bash", """{"command":"uname -a"}"""));
        Assert.Equal("Write  src/App.cs", ToolCallText.Summary("write", """{"path":"src/App.cs","contents":"x"}"""));
        Assert.Equal("Skill  git-release", ToolCallText.Summary("skill", """{"name":"git-release"}"""));
    }

    [Fact]
    public void Summary_IgnoresPartialJson()
    {
        Assert.Equal("Bash", ToolCallText.Summary("bash", """{"com"""));
    }
}
