using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class TranscriptLogTests
{
    [Fact]
    public void Viewport_KeepsLatestRows()
    {
        var log = new TranscriptLog();
        log.Add(TranscriptKind.Note, "one");
        log.Add(TranscriptKind.Note, "two");
        log.Add(TranscriptKind.Note, "three");

        var view = log.Viewport(20, 2, scrollBack: 0);

        Assert.Equal(2, view.Count);
        Assert.Contains("two", view[0].Plain, StringComparison.Ordinal);
        Assert.Contains("three", view[1].Plain, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_RendersCommittedAssistantAsMarkdown()
    {
        var log = new TranscriptLog();
        log.Add(TranscriptKind.Assistant, "# Head");

        var lines = log.BuildLines(40);

        Assert.Contains(lines, line => line.Markup.Contains(Theme.Heading, StringComparison.Ordinal));
    }
}
