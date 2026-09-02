using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ExportSessionArgumentsTests
{
    [Fact]
    public void TryParse_AcceptsMarkdownWithSystemFlag()
    {
        var parsed = ExportSessionArguments.TryParse(
            ["markdown", "./out.md", "--system"],
            out var options,
            out var error);

        Assert.True(parsed);
        Assert.Empty(error);
        Assert.Equal("markdown", options.Format);
        Assert.Equal("./out.md", options.Path);
        Assert.True(options.IncludeSystem);
    }

    [Fact]
    public void TryParse_NormalizesMdAlias()
    {
        ExportSessionArguments.TryParse(["md"], out var options, out _);

        Assert.Equal("markdown", options.Format);
    }

    [Fact]
    public void TryParse_RejectsMultiplePaths()
    {
        var parsed = ExportSessionArguments.TryParse(
            ["json", "first.json", "second.json"],
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Equal("Export accepts at most one path.", error);
    }

    [Fact]
    public void TryParse_RejectsUnknownFormat()
    {
        var parsed = ExportSessionArguments.TryParse(["xml"], out _, out var error);

        Assert.False(parsed);
        Assert.Equal("Unknown export format  xml", error);
    }
}
