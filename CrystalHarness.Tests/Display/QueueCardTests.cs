using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class QueueCardTests
{
    [Fact]
    public void TryCreate_KeepsItemsUntilSent()
    {
        var lines = WidgetPaint.Plain(QueueCard.TryCreate(["first", "second"])!, 48);
        var text = string.Join('\n', lines);

        Assert.Contains("Queued", text, StringComparison.Ordinal);
        Assert.Contains("first", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCreate_EmptyIsNull()
    {
        Assert.Null(QueueCard.TryCreate([]));
    }

    [Fact]
    public void TryCreate_WideCjkItemFitsWidth()
    {
        const int width = 80;
        var lines = WidgetPaint.Lines(QueueCard.TryCreate(["查看其他问题"])!, width);

        Assert.True(lines.Count >= 3);
        foreach (var line in lines)
        {
            Assert.DoesNotContain('\n', line.Plain);
            Assert.True(TextWidth.Measure(line.Plain) <= width);
        }
    }
}
