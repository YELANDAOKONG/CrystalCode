using CrystalCode.Display.Paint;
using CrystalCode.Engine.Events;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class SideQuestionWidgetTests
{
    [Fact]
    public void Create_ShowsTheQuestionAnswerAndCloseHint()
    {
        var snapshot = new SideQuestionSnapshot(
            true,
            [new SideExchange("why", "Because.")],
            false,
            string.Empty,
            string.Empty,
            null);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0), 80));

        Assert.Contains("why", text, StringComparison.Ordinal);
        Assert.Contains("Because.", text, StringComparison.Ordinal);
        Assert.Contains("Esc, Enter, or Space closes", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ShowsWaitingWhileTheQuestionRuns()
    {
        var snapshot = new SideQuestionSnapshot(true, [], true, "why", string.Empty, null);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0), 80));

        Assert.Contains("Waiting for the model", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_NumbersEarlierAnswers()
    {
        var snapshot = new SideQuestionSnapshot(
            true,
            [
                new SideExchange("one", "first"),
                new SideExchange("two", "second"),
                new SideExchange("three", "third"),
                new SideExchange("four", "fourth"),
                new SideExchange("five", "fifth")
            ],
            false,
            string.Empty,
            string.Empty,
            null);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 1), 80));

        Assert.Contains("2/5", text, StringComparison.Ordinal);
        Assert.Contains("two", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
    }
}
