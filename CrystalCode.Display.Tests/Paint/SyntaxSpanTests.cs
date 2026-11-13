using Spectre.Console;

using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

[Collection("Theme")]
public sealed class SyntaxSpanTests : IDisposable
{
    public SyntaxSpanTests() => Theme.Reset();

    public void Dispose() => Theme.Reset();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Render_QueriesWholeNormalizedBlockOnceIncludingOpenFence(bool closed)
    {
        var calls = new List<(string Language, string Text)>();
        var markdown = "```CsHaRp title\r\none\r\ntwo" + (closed ? "\r\n```" : string.Empty);
        var lines = MarkdownRenderer.Render(markdown, 80, (language, text) =>
        {
            calls.Add((language, text));
            return [new SyntaxSpan(1, 5, "comment")];
        });

        Assert.Equal(("csharp", "one\ntwo"), Assert.Single(calls));
        Assert.Contains("CsHaRp title", lines[0].Plain, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.ForSyntax("comment")}]ne[/]", lines[1].Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.ForSyntax("comment")}]tw[/]", lines[2].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain($"[{Theme.ForSyntax("comment")}]    ", lines[2].Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(80)]
    public void Render_TokensPreserveWrappedUnicodeTabsAndMarkup(int width)
    {
        const string text = "a\t中😀e\u0301[red]x[/]\n\t第二行";
        var markdown = "```test\n" + text + "\n```";
        var ordinary = MarkdownRenderer.Render(markdown, width);
        var colored = MarkdownRenderer.Render(markdown, width,
            (_, _) => [new SyntaxSpan(0, text.Length, "string")]);

        Assert.Equal(ordinary.Select(line => line.Plain), colored.Select(line => line.Plain));
        Assert.Equal(ordinary.Select(line => line.Fit(width).Plain), colored.Select(line => line.Fit(width).Plain));
        foreach (var line in colored)
        {
            Assert.Equal(line.Plain, Markup.Remove(line.Markup));
        }

        Assert.Contains(colored, line => line.Markup.Contains(Theme.ForSyntax("string"), StringComparison.Ordinal));
    }

    [Fact]
    public void Render_OffsetsRemainUtf16BeforeTabExpansion()
    {
        var lines = MarkdownRenderer.Render("```test\na\t中😀e\u0301\n```", 80,
            (_, _) => [new SyntaxSpan(3, 2, "string"), new SyntaxSpan(5, 2, "keyword")]);

        Assert.Contains($"[{Theme.ForSyntax("string")}]😀[/]", lines[1].Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.ForSyntax("keyword")}]e\u0301[/]", lines[1].Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_InvalidTokensFallBackWithoutPartialColor()
    {
        const string markdown = "```test\na😀bc\n```";
        IReadOnlyList<SyntaxSpan>[] invalid =
        [
            [new(-1, 1, "keyword")],
            [new(0, 0, "keyword")],
            [new(0, -1, "keyword")],
            [new(0, int.MaxValue, "keyword")],
            [new(int.MaxValue, 1, "keyword")],
            [new(0, 2, "keyword")],
            [new(2, 1, "keyword")],
            [new(0, 1, "keyword"), new(0, 1, "string")],
            [new(3, 1, "keyword"), new(0, 1, "string")],
            [new(0, 1, "red][bold")],
            [new(0, 1, null!)]
        ];
        var ordinary = MarkdownRenderer.Render(markdown, 80);
        foreach (var spans in invalid)
        {
            Assert.Equal(ordinary, MarkdownRenderer.Render(markdown, 80, (_, _) => spans));
        }
    }

    [Fact]
    public void Render_MissingLanguageNullAndFailedCacheUseOrdinaryPaint()
    {
        const string markdown = "```unknown\na\n```";
        Assert.Equal(MarkdownRenderer.Render(markdown, 80),
            MarkdownRenderer.Render(markdown, 80, (_, _) => null));
        Assert.Equal(MarkdownRenderer.Render(markdown, 80),
            MarkdownRenderer.Render(markdown, 80, (_, _) => throw new InvalidOperationException("Unavailable")));
        var calls = 0;
        var noLanguage = MarkdownRenderer.Render("```\na\n```", 80, (_, _) =>
        {
            calls++;
            return [];
        });
        Assert.Equal(0, calls);
        Assert.Equal(MarkdownRenderer.Render("```\na\n```", 80), noLanguage);
    }

    [Theory]
    [InlineData("diff")]
    [InlineData("PATCH extra")]
    [InlineData("csharp")]
    public void Render_DiffLineColorsOverrideTokens(string info)
    {
        const string text = "+added\n-removed\n@@ hunk\n+++ file\n--- file\nplain";
        var lines = MarkdownRenderer.Render($"```{info}\n{text}\n```", 80,
            (_, _) => [new SyntaxSpan(0, text.Length, "keyword")]);

        Assert.Contains($"[{Theme.DiffAdded} on {Theme.DiffAddedBg}]", lines[1].Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.DiffRemoved} on {Theme.DiffRemovedBg}]", lines[2].Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.Accent}]", lines[3].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain($"[{Theme.ForSyntax("keyword")}]", lines[1].Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.ForSyntax("keyword")}]", lines[4].Markup, StringComparison.Ordinal);
        Assert.Contains($"[{Theme.ForSyntax("keyword")}]", lines[6].Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ControlBytesRemainSanitizedAfterHighlighting()
    {
        const string text = "[red]\u001b[31m\u0007中\t😀";
        var markdown = "```test\n" + text + "\n```";
        var ordinary = MarkdownRenderer.Render(markdown, 80);
        var colored = MarkdownRenderer.Render(markdown, 80,
            (_, _) => [new SyntaxSpan(0, text.Length, "string")]);

        Assert.Equal(ordinary.Select(line => line.Fit(80).Plain), colored.Select(line => line.Fit(80).Plain));
        Assert.All(colored, line =>
        {
            var fitted = line.Fit(80);
            Assert.DoesNotContain('\u001b', fitted.Markup);
            Assert.DoesNotContain('\u0007', fitted.Plain);
            Assert.Equal(fitted.Plain, Markup.Remove(fitted.Markup));
        });
    }

    [Theory]
    [InlineData("default")]
    [InlineData("dark")]
    [InlineData("light")]
    public void Theme_SyntaxColorsExistAndAcceptPaletteOverrides(string palette)
    {
        Assert.True(Theme.TryUseBuiltIn(palette));
        string[] kinds = ["keyword", "string", "number", "comment", "type", "function", "operator", "punctuation"];
        foreach (var kind in kinds)
        {
            Assert.Contains("syntax." + kind, Theme.TokenNames);
            _ = new Markup($"[{Theme.ForSyntax(kind)}]sample[/]");
        }

        Assert.True(Theme.TryApply(new Dictionary<string, string> { ["accent"] = "red" }, out var error), error);
        Assert.NotEmpty(Theme.ForSyntax("keyword"));
        // The host resolves fallback256 into this same palette dictionary.
        Assert.True(Theme.TryApply(new Dictionary<string, string> { ["syntax.keyword"] = "blue" }, out error), error);
        Assert.Equal("blue", Theme.ForSyntax("keyword"));
        Assert.Equal(Theme.Code, Theme.ForSyntax("unknown"));
    }
}
