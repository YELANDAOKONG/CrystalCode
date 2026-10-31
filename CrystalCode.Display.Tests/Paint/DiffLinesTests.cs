using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

public sealed class DiffLinesTests
{
    [Fact]
    public void Between_FindsTheChangedRunInsideSharedText()
    {
        var span = DiffSpan.Between("let count = 1;", "let total = 1;");

        Assert.Equal(new DiffSpan(4, 5, 4, 5), span);
    }

    [Fact]
    public void Between_ReportsNothingForEqualOrUnrelatedLines()
    {
        Assert.Null(DiffSpan.Between("same", "same"));
        Assert.Null(DiffSpan.Between("alpha beta gamma", "xyz"));
    }

    [Fact]
    public void Between_HandlesAnInsertionAndADeletion()
    {
        Assert.Equal(new DiffSpan(3, 0, 3, 4), DiffSpan.Between("abcdef", "abcXYZWdef"));
        Assert.Equal(new DiffSpan(3, 4, 3, 0), DiffSpan.Between("abcXYZWdef", "abcdef"));
    }

    [Fact]
    public void Between_NeverSplitsASurrogatePair()
    {
        var span = DiffSpan.Between("name \U0001F600 end", "name \U0001F601 end");

        Assert.NotNull(span);
        var found = span.Value;
        Assert.False(char.IsLowSurrogate("name \U0001F600 end"[found.OldStart]));
    }

    [Fact]
    public void Hunk_MarksTheChangedWordsOnPairedLinesOnly()
    {
        var lines = DiffLines.Hunk(["let count = 1;"], ["let total = 1;", "extra"]);

        Assert.Equal(["-let count = 1;", "+let total = 1;", "+extra"], lines.Select(line => line.Plain));
        Assert.Contains(Theme.DiffRemovedWord, lines[0].Markup, StringComparison.Ordinal);
        Assert.Contains(Theme.DiffAddedWord, lines[1].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Theme.DiffAddedWord, lines[2].Markup, StringComparison.Ordinal);
        Assert.Contains(Theme.DiffAddedBg, lines[2].Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_EscapeMarkupInTheText()
    {
        var line = DiffLines.Added("[red]x[/]", "[red]y[/]");

        Assert.Equal("+[red]x[/]", line.Plain);
        Assert.Contains("[[red]]", line.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_RejectsADiffBackgroundThatDoesNotCombine()
    {
        try
        {
            var applied = Theme.TryApply(
                new Dictionary<string, string> { ["diffAddedBg"] = "green on red" },
                out var error);

            Assert.False(applied);
            Assert.Contains("diffAdded", error, StringComparison.Ordinal);
        }
        finally
        {
            Theme.Reset();
        }
    }
}
