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
}
