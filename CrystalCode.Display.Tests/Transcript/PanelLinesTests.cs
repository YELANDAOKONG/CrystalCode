using CrystalCode.Display.Paint;
using CrystalCode.Display.Transcript;

using Xunit;

namespace CrystalCode.Display.Tests.Transcript;

public sealed class PanelLinesTests
{
    [Theory]
    [InlineData(TranscriptKind.Thinking, 20, "exact")]
    [InlineData(TranscriptKind.Thinking, 36, "stroke-width=\"22\" fill=\"none\" and more text that should wrap onto the next visual row")]
    [InlineData(TranscriptKind.Thinking, 30, "abcdefghij abcdefghij abcdefghij abcdefghij")]
    [InlineData(TranscriptKind.Thinking, 24, "你好世界你好世界你好世界你好世界")]
    [InlineData(TranscriptKind.Thinking, 30, "a\n\nb")]
    [InlineData(TranscriptKind.Thinking, 30, "end\n")]
    [InlineData(TranscriptKind.Thinking, 30, "  leading")]
    [InlineData(TranscriptKind.Thinking, 40, "two  spaces")]
    [InlineData(TranscriptKind.Thinking, 28, "supercalifragilisticexpialidocious-token")]
    [InlineData(TranscriptKind.Thinking, 24, "hi 你好世界你好世界你好世界你好世界你好")]
    [InlineData(TranscriptKind.Thinking, 24, "hi AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(TranscriptKind.Thinking, 30, "say [ok]")]
    [InlineData(TranscriptKind.User, 40, "hello")]
    [InlineData(TranscriptKind.Tool, 32, "read file")]
    [InlineData(TranscriptKind.Error, 32, "boom")]
    [InlineData(TranscriptKind.Thinking, 80, "hello\nworld\n")]
    public void TryCreate_MatchesSpectreCard(TranscriptKind kind, int width, string text)
    {
        var expected = string.Join(
            '\n',
            WidgetPaint.Lines(TranscriptCard.TryCreate(kind, text)!, width).Select(line => line.Markup));
        var actual = string.Join(
            '\n',
            PanelLines.TryCreate(kind, text, width)!.Select(line => line.Markup));

        Assert.Equal(expected, actual);
    }
}
