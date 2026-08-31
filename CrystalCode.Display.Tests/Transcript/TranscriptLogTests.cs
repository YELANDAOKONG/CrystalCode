using CrystalCode.Display.Paint;
using CrystalCode.Display.Transcript;

using Xunit;

namespace CrystalCode.Display.Tests.Transcript;

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

    [Fact]
    public void BuildLines_RendersLiveAssistantAsMarkdown()
    {
        var log = new TranscriptLog();
        log.AppendLive(TranscriptKind.Assistant, "# Head");

        var lines = log.BuildLines(40);

        Assert.Contains(lines, line => line.Markup.Contains(Theme.Heading, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("Head", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildLines_FramesUserMessage()
    {
        var log = new TranscriptLog();
        log.Add(TranscriptKind.User, "hello");

        var text = string.Join('\n', log.BuildLines(40).Select(line => line.Plain));

        Assert.Contains("You", text, StringComparison.Ordinal);
        Assert.Contains("hello", text, StringComparison.Ordinal);
    }
}
