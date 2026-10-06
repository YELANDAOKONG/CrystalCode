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
    public void DiscardLive_DropsUncommittedText()
    {
        var log = new TranscriptLog();
        log.AppendLive(TranscriptKind.Assistant, "partial");
        log.DiscardLive();
        log.Add(TranscriptKind.Note, "kept");

        var text = string.Join('\n', log.BuildLines(40).Select(line => line.Plain));

        Assert.DoesNotContain("partial", text, StringComparison.Ordinal);
        Assert.Contains("kept", text, StringComparison.Ordinal);
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

    [Fact]
    public void BuildLines_LiveThinkingAppendMatchesTheFullCard()
    {
        const int width = 36;
        var text = "stroke-width=\"22\" fill=\"none\" and more text that should wrap onto the next visual row\n"
            + "hi " + new string('你', 12) + "\n\n"
            + "supercalifragilisticexpialidocious-token";
        var log = new TranscriptLog();
        for (var i = 0; i < text.Length;)
        {
            var take = Math.Min(3, text.Length - i);
            log.AppendLive(TranscriptKind.Thinking, text.Substring(i, take));
            i += take;
            _ = log.BuildLines(width);
        }

        var actual = string.Join('\n', log.BuildLines(width).Select(line => line.Markup));
        var expected = string.Join(
            '\n',
            WidgetPaint.Lines(TranscriptCard.TryCreate(TranscriptKind.Thinking, text)!, width)
                .Select(line => line.Markup));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Viewport_LiveThinkingScrollsWithoutLosingTheTail()
    {
        var log = new TranscriptLog();
        var text = string.Join('\n', Enumerable.Range(0, 40).Select(i => "line " + i));
        log.AppendLive(TranscriptKind.Thinking, text);

        var tail = log.Viewport(40, 8, scrollBack: 0);
        var head = log.Viewport(40, 8, scrollBack: 10_000);

        Assert.Contains("line 39", tail[^2].Plain, StringComparison.Ordinal);
        Assert.Contains("Thinking", head[0].Plain, StringComparison.Ordinal);
        Assert.Contains("line 0", string.Join('\n', head.Select(line => line.Plain)), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_ExpandsTabsInsideAThinkingCard()
    {
        var log = new TranscriptLog();
        log.AppendLive(TranscriptKind.Thinking, "a\tb");

        var plain = string.Join('\n', log.BuildLines(40).Select(line => line.Plain));

        Assert.Contains("a    b", plain, StringComparison.Ordinal);
        Assert.Contains("│", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_StripsAnsiAndKeepsTheVisibleText()
    {
        var log = new TranscriptLog();
        log.Add(TranscriptKind.Note, "\u001b[31mhello\u001b[0m");

        var text = string.Join('\n', log.BuildLines(40).Select(line => line.Plain));

        Assert.Contains("hello", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendLive_JoinsASplitEscapeWithoutRewritingEarlierText()
    {
        var log = new TranscriptLog();
        log.AppendLive(TranscriptKind.Thinking, "keep\u001b[3");
        log.AppendLive(TranscriptKind.Thinking, "1m");

        var plain = string.Join('\n', log.BuildLines(40).Select(line => line.Plain));

        Assert.Contains("keep", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", plain, StringComparison.Ordinal);
    }
}
