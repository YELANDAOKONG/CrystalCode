using CrystalCode.Display.Shell;
using CrystalCode.Display.Transcript;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ScrollAnchorTests
{
    [Fact]
    public void Resolve_FollowsTheBottomWhenNotScrolled()
    {
        var anchor = new ScrollAnchor();

        Assert.Equal(0, anchor.Resolve(80, 30, 0));
        Assert.Equal(0, anchor.Resolve(80, 40, 0));
    }

    [Fact]
    public void Resolve_AddsNewRowsToTheDistanceWhileScrolledBack()
    {
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, 30, 0);

        Assert.Equal(10, anchor.Resolve(80, 30, 10));
        Assert.Equal(12, anchor.Resolve(80, 32, 10));
        Assert.Equal(12, anchor.Resolve(80, 32, 12));
    }

    [Fact]
    public void Resolve_KeepsTheDistanceWhenRowsShrink()
    {
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, 30, 5);

        Assert.Equal(5, anchor.Resolve(80, 28, 5));
    }

    [Fact]
    public void Resolve_KeepsTheDistanceAfterAWidthChange()
    {
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, 30, 5);

        Assert.Equal(5, anchor.Resolve(100, 45, 5));
    }

    [Fact]
    public void Resolve_IgnoresTheFirstPaint()
    {
        var anchor = new ScrollAnchor();

        Assert.Equal(7, anchor.Resolve(80, 30, 7));
    }

    [Fact]
    public void Reset_ForgetsThePreviousRowCount()
    {
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, 30, 0);
        anchor.Reset();

        Assert.Equal(4, anchor.Resolve(80, 60, 4));
    }

    [Fact]
    public void TheViewportStaysOnTheSameRowsWhileOutputArrives()
    {
        var log = new TranscriptLog();
        for (var i = 0; i < 30; i++)
        {
            log.Add(TranscriptKind.Note, "line " + i);
        }

        var anchor = new ScrollAnchor();
        var scrollBack = anchor.Resolve(80, log.RowCount(80), 0);
        scrollBack = anchor.Resolve(80, log.RowCount(80), scrollBack + 10);
        var before = log.Viewport(80, 5, scrollBack).Select(static line => line.Plain.Trim()).ToArray();

        log.Add(TranscriptKind.Note, "new a");
        log.Add(TranscriptKind.Note, "new b");
        scrollBack = anchor.Resolve(80, log.RowCount(80), scrollBack);
        var after = log.Viewport(80, 5, scrollBack).Select(static line => line.Plain.Trim()).ToArray();

        Assert.Equal(before, after);
    }
}
