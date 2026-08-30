using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void Render_EmitsHeadingListAndFence()
    {
        const string markdown =
            """
            # Title
            - item
            ```
            code
            ```
            Use `Read` and **bold**.
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Contains(lines, line => line.Plain.Contains("Title", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("* item", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("code", StringComparison.Ordinal)
            && line.Markup.Contains(Theme.Code, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Markup.Contains(Theme.Code, StringComparison.Ordinal)
            && line.Markup.Contains("Read", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Markup.Contains("[bold]", StringComparison.Ordinal));
    }
}
