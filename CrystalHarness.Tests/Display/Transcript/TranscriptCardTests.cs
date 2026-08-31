using CrystalHarness.Display.Paint;
using CrystalHarness.Display.Transcript;

using Xunit;

namespace CrystalHarness.Tests.Display.Transcript;

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
    public void TryCreate_SkipsNotes()
    {
        Assert.Null(TranscriptCard.TryCreate(TranscriptKind.Note, "compacted"));
    }
}
