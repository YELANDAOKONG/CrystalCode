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

    [Fact]
    public void Render_EmitsOrderedListAndBlockquote()
    {
        const string markdown =
            """
            1. first item
            2. second item
            > this is a blockquote
            ---
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Contains(lines, line => line.Plain.Contains("1. first item", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("2. second item", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("│ this is a blockquote", StringComparison.Ordinal)
            && line.Markup.Contains(Theme.Muted, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Markup.Contains(Theme.Rule, StringComparison.Ordinal));
    }

    [Fact]
    public void Render_EmitsCodeFenceWithLanguageAndDiffHighlights()
    {
        const string markdown =
            """
            ```csharp
            + public int Add(int a, int b)
            - public int Add()
              return a + b;
            ```
            """;

        var lines = MarkdownRenderer.Render(markdown, 60);

        Assert.Contains(lines, line => line.Markup.Contains("csharp", StringComparison.Ordinal)
            && line.Markup.Contains(Theme.Muted, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Markup.Contains(Theme.DiffAdded, StringComparison.Ordinal)
            && line.Plain.Contains("+ public int Add(int a, int b)", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Markup.Contains(Theme.DiffRemoved, StringComparison.Ordinal)
            && line.Plain.Contains("- public int Add()", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_EmitsInlineStyles()
    {
        const string markdown = "This is *italic*, ~~strikethrough~~, and `inline code`.";

        var lines = MarkdownRenderer.Render(markdown, 80);

        Assert.Single(lines);
        Assert.Contains("[italic]italic[/]", lines[0].Markup, StringComparison.Ordinal);
        Assert.Contains("[strikethrough]strikethrough[/]", lines[0].Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Code}]inline code[/]", lines[0].Markup, StringComparison.Ordinal);
    }
}
