using CrystalCode.Display.Paint;
using CrystalCode.Display.Transcript;

using Xunit;

namespace CrystalCode.Display.Tests.Transcript;

public sealed class TranscriptCardTests
{
    [Fact]
    public void TryCreate_FramesUserAndThinking()
    {
        var user = string.Join('\n', WidgetPaint.Plain(TranscriptCard.TryCreate(TranscriptKind.User, "hello")!, 40));
        var thinking = string.Join(
            '\n',
            WidgetPaint.Plain(TranscriptCard.TryCreate(TranscriptKind.Thinking, "hmm")!, 40));
        var result = string.Join(
            '\n',
            WidgetPaint.Plain(TranscriptCard.TryCreate(TranscriptKind.Result, "7.0.0-30")!, 40));

        Assert.Contains("You", user, StringComparison.Ordinal);
        Assert.Contains("hello", user, StringComparison.Ordinal);
        Assert.Contains("Thinking", thinking, StringComparison.Ordinal);
        Assert.Contains("Result", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_ColorsTodoMarksByStatus()
    {
        var lines = WidgetPaint.Lines(
            TranscriptCard.TryCreate(
                TranscriptKind.Result,
                "- [ ] wait\n- [~] now\n- [x] done\n- [-] skip")!,
            48);

        var wait = lines.Single(line => line.Plain.Contains("wait", StringComparison.Ordinal));
        var now = lines.Single(line => line.Plain.Contains("now", StringComparison.Ordinal));
        var done = lines.Single(line => line.Plain.Contains("done", StringComparison.Ordinal));
        var skip = lines.Single(line => line.Plain.Contains("skip", StringComparison.Ordinal));

        Assert.Contains(Theme.Chrome, wait.Markup, StringComparison.Ordinal);
        Assert.Contains(Theme.Accent, now.Markup, StringComparison.Ordinal);
        Assert.Contains(Theme.Ok, done.Markup, StringComparison.Ordinal);
        Assert.Contains(Theme.Muted, skip.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Theme.DiffRemoved, wait.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(Theme.DiffRemoved, now.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_SkipsNotes()
    {
        Assert.Null(TranscriptCard.TryCreate(TranscriptKind.Note, "compacted"));
    }
}
