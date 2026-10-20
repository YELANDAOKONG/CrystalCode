using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

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

    [Fact]
    public void Split_PreservesWindowsSeparatorsInsideDoubleQuotes()
    {
        var tokens = CommandArguments.Split("""json "C:\Users\runner\My Exports\child-export.json" --system""");

        Assert.Equal(["json", @"C:\Users\runner\My Exports\child-export.json", "--system"], tokens);
    }

    [Fact]
    public void Split_PreservesWindowsSeparatorsOutsideQuotes()
    {
        var tokens = CommandArguments.Split(@"json C:\Users\runner\child-export.json");

        Assert.Equal(["json", @"C:\Users\runner\child-export.json"], tokens);
    }

    [Fact]
    public void Split_PreservesUncPrefix()
    {
        var tokens = CommandArguments.Split(@"json \\server\share\child-export.json");

        Assert.Equal(["json", @"\\server\share\child-export.json"], tokens);
    }
}
