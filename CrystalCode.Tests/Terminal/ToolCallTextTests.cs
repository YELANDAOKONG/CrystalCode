using CrystalCode.Engine.Sessions;

using Xunit;
using CrystalCode.Terminal;

namespace CrystalCode.Tests.Terminal;

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

    [Fact]
    public void Summary_SpellsOutHiddenCharactersInCommands()
    {
        var summary = ToolCallText.Summary(
            "bash",
            """{"command":"echo safe\u001b[1G\u001b[2Kls\u202e"}""");

        Assert.Equal("Bash  echo safe\\x1b[1G\\x1b[2Kls\\u202e", summary);
    }

    [Fact]
    public void Summary_SpellsOutHiddenCharactersInPathsAndFlattensBreaks()
    {
        var summary = ToolCallText.Summary("read", """{"path":"a\u001b]0;x\u0007\nb"}""");

        Assert.Equal("Read  a\\x1b]0;x\\x07 b", summary);
    }
}
