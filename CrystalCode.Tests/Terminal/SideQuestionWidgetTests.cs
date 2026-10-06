using Xunit;

using CrystalCode.Display.Paint;
using CrystalCode.Display.Shell;
using CrystalCode.Engine.Events;
using CrystalCode.Terminal;

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
            null,
            false);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0), 80));

        Assert.Contains("why", text, StringComparison.Ordinal);
        Assert.Contains("Because.", text, StringComparison.Ordinal);
        Assert.Contains("Esc, Enter, Space, or Ctrl+C closes", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ShowsWaitingWhileTheQuestionRuns()
    {
        var snapshot = new SideQuestionSnapshot(true, [], true, "why", string.Empty, null, false);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0), 80));

        Assert.Contains(SideQuestionWidget.WaitingCaption, text, StringComparison.Ordinal);
        Assert.Contains(ProgressSpinner.Frame(0), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AdvancesTheWaitingSpinner()
    {
        var snapshot = new SideQuestionSnapshot(true, [], true, "why", string.Empty, null, false);

        var first = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0, 0), 80));
        var next = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0, 1), 80));

        Assert.Contains(ProgressSpinner.Frame(0), first, StringComparison.Ordinal);
        Assert.DoesNotContain(ProgressSpinner.Frame(1), first, StringComparison.Ordinal);
        Assert.Contains(ProgressSpinner.Frame(1), next, StringComparison.Ordinal);
        Assert.Contains(SideQuestionWidget.WaitingCaption, next, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ShowsThinkingWhileReasoning()
    {
        var snapshot = new SideQuestionSnapshot(true, [], true, "why", string.Empty, null, true);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0), 80));

        Assert.Contains(SideQuestionWidget.ThinkingCaption, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SideQuestionWidget.WaitingCaption, text, StringComparison.Ordinal);
        Assert.Contains(ProgressSpinner.Frame(0), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_ShowsTheAnswerOnceTextArrives()
    {
        var snapshot = new SideQuestionSnapshot(true, [], true, "why", "Because.", null, true);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 0), 80));

        Assert.Contains("Because.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(SideQuestionWidget.ThinkingCaption, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SideQuestionWidget.WaitingCaption, text, StringComparison.Ordinal);
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
            null,
            false);

        var text = string.Join('\n', WidgetPaint.Plain(SideQuestionWidget.Create(snapshot, 1), 80));

        Assert.Contains("2/5", text, StringComparison.Ordinal);
        Assert.Contains("two", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_IndentsLikeOtherCardsAndKeepsATabInsideTheBorder()
    {
        const int width = 80;
        var snapshot = new SideQuestionSnapshot(
            true,
            [new SideExchange("q", "col1\tcol2\n" + new string('a', 72) + "\tZ")],
            false,
            string.Empty,
            string.Empty,
            null,
            false);

        var lines = WidgetPaint.Lines(SideQuestionWidget.Create(snapshot, 0), width);

        Assert.StartsWith("  ", lines[0].Plain, StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Plain.Contains("col1    col2", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains('Z'));
        Assert.All(lines, line =>
        {
            Assert.True(TextWidth.Measure(line.Plain) <= width);
            Assert.DoesNotContain("...", line.Plain);
            Assert.DoesNotContain('\t', line.Plain);
        });
    }
}
