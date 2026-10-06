using CrystalCode.Display.Shell;
using CrystalCode.Display.Transcript;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class ScrollAnchorTests
{
    [Fact]
    public void Resolve_FollowsTheBottomWhenNotScrolled()
    {
        var log = Notes(30);
        var anchor = new ScrollAnchor();

        Assert.Equal(0, anchor.Resolve(80, log, 5, 0));
        log.Add(TranscriptKind.Note, "more");

        Assert.Equal(0, anchor.Resolve(80, log, 5, 0));
    }

    [Fact]
    public void Resolve_AddsNewRowsToTheDistanceWhileScrolledBack()
    {
        var log = Notes(30);
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, log, 5, 0);

        Assert.Equal(10, anchor.Resolve(80, log, 5, 10));
        log.Add(TranscriptKind.Note, "new a");
        log.Add(TranscriptKind.Note, "new b");
        Assert.Equal(12, anchor.Resolve(80, log, 5, 10));
        Assert.Equal(12, anchor.Resolve(80, log, 5, 12));
    }

    [Fact]
    public void Resolve_AddsTheWheelAndTheAppendedRows()
    {
        var log = Notes(30);
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, log, 5, 0);
        var scroll = anchor.Resolve(80, log, 5, 10);
        log.Add(TranscriptKind.Note, "new a");
        log.Add(TranscriptKind.Note, "new b");

        Assert.Equal(15, anchor.Resolve(80, log, 5, scroll + 3));
    }

    [Fact]
    public void Resolve_MeasuresANewScrollFromTheLatestBottom()
    {
        var log = Notes(30);
        var anchor = new ScrollAnchor();
        var scroll = anchor.Resolve(80, log, 5, 0);
        for (var i = 0; i < 10; i++)
        {
            log.Add(TranscriptKind.Note, "late " + i);
        }

        Assert.Equal(3, anchor.Resolve(80, log, 5, scroll + 3));
    }

    [Fact]
    public void Resolve_FollowsTheBottomAfterAnExplicitJump()
    {
        var log = Notes(30);
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, log, 5, 0);
        anchor.Resolve(80, log, 5, 10);
        log.Add(TranscriptKind.Note, "new a");

        Assert.Equal(0, anchor.Resolve(80, log, 5, 0));
    }

    [Fact]
    public void Resolve_KeepsTheSameLinesWhenTheTailShrinks()
    {
        var log = Notes(20);
        log.AppendLive(TranscriptKind.Note, "live");
        var anchor = new ScrollAnchor();
        var scroll = anchor.Resolve(80, log, 5, 0);
        scroll = anchor.Resolve(80, log, 5, 4);
        var before = Plains(log, 80, 5, scroll);

        log.DiscardLive();
        scroll = anchor.Resolve(80, log, 5, scroll);

        Assert.Equal(3, scroll);
        Assert.Equal(before, Plains(log, 80, 5, scroll));
    }

    [Fact]
    public void Resolve_KeepsTheAnchoredRowWhenAnEarlierBlockGrows()
    {
        var log = new TranscriptLog { VerboseTools = false };
        log.Add(TranscriptKind.Result, "hidden one\nhidden two", toolName: "read");
        log.Add(TranscriptKind.Note, "anchor");
        log.Add(TranscriptKind.Note, "tail");
        var anchor = new ScrollAnchor();
        var scroll = anchor.Resolve(80, log, 1, 0);
        scroll = anchor.Resolve(80, log, 1, scroll + 1);
        Assert.Contains("anchor", Visible(log, 80, 1, scroll), StringComparison.Ordinal);

        log.VerboseTools = true;
        scroll = anchor.Resolve(80, log, 1, scroll);
        var visible = Visible(log, 80, 1, scroll);

        Assert.Contains("anchor", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", visible, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_KeepsTheDistanceAfterAWidthChange()
    {
        var log = new TranscriptLog();
        for (var i = 0; i < 20; i++)
        {
            log.Add(TranscriptKind.Note, new string('x', 80));
        }

        var anchor = new ScrollAnchor();
        var scroll = anchor.Resolve(40, log, 5, 0);
        scroll = anchor.Resolve(40, log, 5, 4);

        Assert.Equal(4, anchor.Resolve(100, log, 5, scroll));
    }

    [Fact]
    public void Resolve_IgnoresTheFirstPaint()
    {
        var anchor = new ScrollAnchor();

        Assert.Equal(7, anchor.Resolve(80, Notes(30), 10, 7));
    }

    [Fact]
    public void Reset_ForgetsThePreviousAnchor()
    {
        var log = Notes(30);
        var anchor = new ScrollAnchor();
        anchor.Resolve(80, log, 5, 0);
        anchor.Resolve(80, log, 5, 10);
        anchor.Reset();
        for (var i = 0; i < 30; i++)
        {
            log.Add(TranscriptKind.Note, "extra " + i);
        }

        Assert.Equal(4, anchor.Resolve(80, log, 5, 4));
    }

    [Fact]
    public void TheViewportStaysOnTheSameRowsWhileOutputArrives()
    {
        var log = Notes(30);
        var anchor = new ScrollAnchor();
        var scrollBack = anchor.Resolve(80, log, 5, 0);
        scrollBack = anchor.Resolve(80, log, 5, scrollBack + 10);
        var before = Plains(log, 80, 5, scrollBack);

        log.Add(TranscriptKind.Note, "new a");
        log.Add(TranscriptKind.Note, "new b");
        scrollBack = anchor.Resolve(80, log, 5, scrollBack);
        var after = Plains(log, 80, 5, scrollBack);

        Assert.Equal(before, after);
    }

    private static TranscriptLog Notes(int count)
    {
        var log = new TranscriptLog();
        for (var i = 0; i < count; i++)
        {
            log.Add(TranscriptKind.Note, "line " + i);
        }

        return log;
    }

    private static string[] Plains(TranscriptLog log, int width, int rows, int scroll) =>
        log.Viewport(width, rows, scroll).Select(static line => line.Plain).ToArray();

    private static string Visible(TranscriptLog log, int width, int rows, int scroll) =>
        string.Join('\n', Plains(log, width, rows, scroll));
}
