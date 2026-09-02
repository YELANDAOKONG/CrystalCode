using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class CommandArgumentsTests
{
    [Fact]
    public void Split_PreservesSpacesInsideDoubleQuotes()
    {
        var tokens = CommandArguments.Split("markdown \"./my exports/a.md\" --system");

        Assert.Equal(["markdown", "./my exports/a.md", "--system"], tokens);
    }

    [Fact]
    public void Split_PreservesSpacesInsideSingleQuotes()
    {
        var tokens = CommandArguments.Split("export './prompt templates'");

        Assert.Equal(["export", "./prompt templates"], tokens);
    }

    [Fact]
    public void Split_SupportsBackslashEscapesOutsideQuotes()
    {
        var tokens = CommandArguments.Split(@"markdown ./my\ exports/a.md");

        Assert.Equal(["markdown", "./my exports/a.md"], tokens);
    }

    [Fact]
    public void Split_SupportsDoubleQuoteEscapes()
    {
        var tokens = CommandArguments.Split("""markdown "./a \"quoted\".md" """);

        Assert.Equal(["markdown", "./a \"quoted\".md"], tokens);
    }

    [Fact]
    public void Split_ThrowsWhenQuoteIsUnclosed()
    {
        Assert.Throws<ArgumentException>(() => CommandArguments.Split("markdown \"./open.md"));
    }
}
