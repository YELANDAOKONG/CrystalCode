using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

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
            && line.Markup.Contains(Theme.Code, StringComparison.Ordinal)
            && line.Markup.Contains(Theme.CodeBg, StringComparison.Ordinal));
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

    [Fact]
    public void Render_LeavesIdentifierUnderscoresLiteral()
    {
        var lines = MarkdownRenderer.Render("Call __init__ and snake_case_name.", 80);

        var line = Assert.Single(lines);
        Assert.Contains("__init__", line.Plain, StringComparison.Ordinal);
        Assert.Contains("snake_case_name", line.Plain, StringComparison.Ordinal);
        Assert.DoesNotContain("[italic]", line.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("[underline]", line.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_KeepsBoldAcrossWrappedRows()
    {
        var lines = MarkdownRenderer.Render("**" + new string('a', 30) + "**", 20);

        Assert.True(lines.Count > 1);
        Assert.All(lines, line =>
        {
            Assert.Contains("[bold]", line.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("**", line.Plain, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Render_AlignsATableByDisplayColumns()
    {
        const string markdown =
            """
            | 概念 | 值 |
            |---|---|
            | 甲 | 乙 |
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Equal(
            ["  概念  值", "  ────  ──", "  甲    乙"],
            lines.Select(line => line.Plain));
        Assert.Contains(Theme.Heading, lines[0].Markup, StringComparison.Ordinal);
        Assert.Contains(Theme.Muted, lines[1].Markup, StringComparison.Ordinal);
        Assert.All(lines, line => Assert.DoesNotContain("|", line.Plain, StringComparison.Ordinal));
    }

    [Fact]
    public void Render_WrapsAWideCellInsideItsColumn()
    {
        const string markdown =
            """
            | 名 | 说明 |
            |---|---|
            | 甲 | 一二三四五六七八 |
            """;

        var lines = MarkdownRenderer.Render(markdown, 16);
        var wrapped = lines.Single(line => line.Plain.Contains('六'));
        var wrappedAt = wrapped.Plain.IndexOf('六');

        Assert.Contains(lines, line => line.Plain == "  甲  一二三四五");
        Assert.Equal("      六七八", wrapped.Plain.TrimEnd());
        Assert.Equal(6, TextWidth.Measure(wrapped.Plain.AsSpan(0, wrappedAt)));
    }

    [Fact]
    public void Render_SharesOverflowAcrossWideColumns()
    {
        const string markdown =
            """
            | item | long | mid | side |
            |---|---|---|---|
            | name | aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa | bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb | cccccccccccccccccccccccccccccc |
            """;

        var lines = MarkdownRenderer.Render(markdown, 70);

        Assert.Contains(lines, line =>
            line.Plain.Contains("name", StringComparison.Ordinal)
            && line.Plain.Contains(new string('a', 12), StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Plain.Contains(new string('b', 30), StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Plain.Contains(new string('c', 30), StringComparison.Ordinal));
        Assert.True(lines.Count <= 12);
        Assert.All(lines, line =>
        {
            Assert.True(TextWidth.Measure(line.Plain) <= 70);
            Assert.DoesNotContain("|", line.Plain, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Render_HonorsRightAlignmentAndInlineStyles()
    {
        const string markdown =
            """
            | item | n |
            | :--- | ---: |
            | a | 12 |
            | **go** | `ok` |
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Contains(lines, line => line.Plain == "  item   n");
        Assert.Contains(lines, line => line.Plain == "  a     12");
        Assert.Contains(lines, line => line.Plain == "  go    ok"
            && line.Markup.Contains("[bold]go[/]", StringComparison.Ordinal)
            && line.Markup.Contains($"[{Theme.Code}]ok[/]", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_KeepsAnEscapedPipeAndAPipeInsideCodeInOneCell()
    {
        const string markdown =
            """
            | a | b |
            |---|---|
            | left\|right | z |
            | `x|y` | z |
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Contains(lines, line => line.Plain.Contains("left|right", StringComparison.Ordinal)
            && line.Plain.Contains('z'));
        Assert.Contains(lines, line => line.Plain.Contains("x|y", StringComparison.Ordinal)
            && line.Markup.Contains(Theme.Code, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotContain("\\", line.Plain, StringComparison.Ordinal));
    }

    [Fact]
    public void Render_FallsBackToOriginalLinesWhenARowDoesNotMatch()
    {
        const string markdown =
            """
            | a | b |
            |---|---|
            | only |

            after
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Contains(lines, line => line.Plain.Contains("| a | b |", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("|---|---|", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("| only |", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("after", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_FallsBackWhenTheSeparatorWidthDiffers()
    {
        const string markdown =
            """
            | a | b |
            |---|
            | 1 | 2 |
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Contains(lines, line => line.Plain.Contains("| a | b |", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("|---|", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("| 1 | 2 |", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_StacksColumnsThatCannotMeetTheirMinimum()
    {
        const string markdown =
            """
            | aa | bb | cc |
            |---|---|---|
            | one | two | three |
            """;

        var lines = MarkdownRenderer.Render(markdown, 12);

        Assert.Equal(
            ["  aa", "  one", "  bb", "  two", "  cc", "  three"],
            lines.Select(line => line.Plain));
        Assert.Contains(Theme.Heading, lines[0].Markup, StringComparison.Ordinal);
        Assert.All(lines, line => Assert.DoesNotContain("|", line.Plain, StringComparison.Ordinal));
    }

    [Fact]
    public void Render_FallsBackWhenACellIsWiderThanTheTerminal()
    {
        const string markdown =
            """
            | 甲 |
            |---|
            | 乙 |
            """;

        var lines = MarkdownRenderer.Render(markdown, 3);

        Assert.Contains(lines, line => line.Plain.Contains('|'));
    }

    [Fact]
    public void Render_LeavesAPipeRowWithoutASeparatorAsText()
    {
        var lines = MarkdownRenderer.Render("use a | b here", 40);

        var line = Assert.Single(lines);
        Assert.Contains("use a | b here", line.Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_LeavesATableInsideAFenceUntouched()
    {
        const string markdown =
            """
            ```
            | a | b |
            |---|---|
            ```
            """;

        var lines = MarkdownRenderer.Render(markdown, 40);

        Assert.Contains(lines, line => line.Plain.Contains("| a | b |", StringComparison.Ordinal)
            && line.Markup.Contains(Theme.CodeBg, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("|---|---|", StringComparison.Ordinal));
    }
}
