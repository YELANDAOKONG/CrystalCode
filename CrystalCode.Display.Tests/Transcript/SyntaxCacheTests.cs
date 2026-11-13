using CrystalCode.Display.Paint;
using CrystalCode.Display.Transcript;

using Xunit;

namespace CrystalCode.Display.Tests.Transcript;

public sealed class SyntaxCacheTests
{
    [Fact]
    public void InvalidateCache_RepaintsCommittedAndLiveAssistantWithoutDroppingContent()
    {
        var ready = false;
        var calls = 0;
        var log = new TranscriptLog
        {
            SyntaxTokens = (_, _) =>
            {
                calls++;
                return ready ? [new SyntaxSpan(0, 4, "keyword")] : null;
            }
        };
        log.Add(TranscriptKind.Assistant, "```test\ncode\n```");
        log.AppendLive(TranscriptKind.Assistant, "```test\ncode");
        var before = log.BuildLines(80).ToArray();
        Assert.Equal(2, calls);
        _ = log.BuildLines(80);
        Assert.Equal(2, calls);

        ready = true;
        log.InvalidateCache();
        var after = log.BuildLines(80).ToArray();
        Assert.Equal(4, calls);
        Assert.Equal(before.Select(line => line.Plain), after.Select(line => line.Plain));
        Assert.Equal(2, after.Count(line => line.Markup.Contains($"[{Theme.ForSyntax("keyword")}]code[/]", StringComparison.Ordinal)));

        log.CommitLive();
        Assert.Equal(after, log.BuildLines(80));
        Assert.Equal(4, calls);
    }

    [Fact]
    public void BuildLines_QueriesOnlyAssistantEntries()
    {
        var calls = 0;
        var log = new TranscriptLog
        {
            SyntaxTokens = (_, _) =>
            {
                calls++;
                return [];
            }
        };
        foreach (var kind in new[] { TranscriptKind.User, TranscriptKind.Thinking, TranscriptKind.Result, TranscriptKind.Error, TranscriptKind.Note })
        {
            log.Add(kind, "```test\ncode\n```");
        }

        _ = log.BuildLines(80);
        Assert.Equal(0, calls);
        log.Add(TranscriptKind.Assistant, "```test\ncode\n```");
        _ = log.BuildLines(80);
        Assert.Equal(1, calls);
    }
}
