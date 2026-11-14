using System.Collections.Concurrent;
using System.Reflection;

using CrystalCode.Display.Paint;
using CrystalCode.Display.Transcript;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Home.Settings;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class TerminalSyntaxCacheTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Create_BindsEngineSyntaxReadyAndDisplayCallbackToTheSameCache()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        TerminalSyntaxCache? cache = null;
        CodingSession? session = null;
        try
        {
            var home = new CrystalHome(directory.FullName);
            var settings = HarnessSettings.CreateDefault().WithOverrides("ollama", "qwen3:8b");
            var launch = new SessionLaunch(home, new DiskSettingsStore(home), new CredentialStore(home),
                settings, settings, directory.FullName, null, null, [], []);
            var host = TerminalHost.Create(launch, PluginRegistry.CreateBuiltIn());
            cache = Field<TerminalSyntaxCache>(host, "_syntaxCache");
            session = Field<CodingSession>(host, "_session");
            var frontEnd = Field<SessionFrontEnd>(session, "_frontEnd");
            var renderer = Field<SessionRenderer>(host, "_renderer");
            var log = Field<TranscriptLog>(renderer, "_log");
            Assert.Same(cache, frontEnd.SyntaxReady!.Target);
            Assert.Same(cache, log.SyntaxTokens!.Target);
        }
        finally
        {
            cache?.Dispose();
            if (session is not null)
            {
                await session.CloseAsync();
            }

            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetTokens_MissCompletesOffCallerAndCachesPositiveOrNegative(bool negative)
    {
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<IReadOnlyList<SyntaxToken>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        cache.SetService((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            started.SetResult();
            return result.Task;
        }, _ => true);
        cache.ConsumeRefresh();

        Assert.Null(cache.GetTokens("CS", "let"));
        await started.Task.WaitAsync(Timeout);
        Assert.Null(cache.GetTokens("cs", "let"));
        result.SetResult(negative ? null : [new SyntaxToken(0, 3, "keyword")]);
        await UntilAsync(() => cache.Counts.Entries == 1);
        Assert.Equal(!negative, cache.ConsumeRefresh());
        Assert.False(cache.ConsumeRefresh());
        var spans = cache.GetTokens("cs", "let");
        if (negative)
        {
            Assert.Null(spans);
        }
        else
        {
            Assert.Equal(new SyntaxSpan(0, 3, "keyword"), Assert.Single(spans!));
        }

        Assert.Equal(1, calls);
        Assert.Equal(0, cache.Counts.Queued);
    }

    [Fact]
    public async Task SetService_CancelsActiveWaitsDropsLateResultsAndClearsEntries()
    {
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<IReadOnlyList<SyntaxToken>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.SetService((_, _, token) =>
        {
            started.SetResult(token);
            return late.Task;
        }, _ => true);
        cache.GetTokens("cs", "old");
        var oldToken = await started.Task.WaitAsync(Timeout);
        cache.SetService((_, _, _) => Task.FromResult<IReadOnlyList<SyntaxToken>?>(
            [new SyntaxToken(0, 3, "string")]), _ => true);
        await UntilAsync(() => oldToken.IsCancellationRequested);
        cache.GetTokens("cs", "new");
        await UntilAsync(() => cache.Counts.Entries == 1);
        late.SetResult([new SyntaxToken(0, 3, "keyword")]);
        Assert.Equal("string", Assert.Single(cache.GetTokens("cs", "new")!).Kind);
        Assert.Null(cache.GetTokens("cs", "old"));
    }

    [Fact]
    public async Task ConsumeRefresh_RetiredServiceRemovesPreviouslyColoredEntries()
    {
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        var supported = true;
        cache.SetService((_, _, _) => Task.FromResult<IReadOnlyList<SyntaxToken>?>(
            [new SyntaxToken(0, 3, "keyword")]), _ => supported);
        cache.GetTokens("cs", "let");
        await UntilAsync(() => cache.Counts.Entries == 1);
        cache.ConsumeRefresh();
        supported = false;
        Assert.True(cache.ConsumeRefresh());
        Assert.Equal(0, cache.Counts.Entries);
        Assert.Null(cache.GetTokens("cs", "let"));
    }

    [Fact]
    public async Task ConsumeRefresh_DisabledLanguagePreservesHealthyTokensAndViewportColors()
    {
        using var renderer = new SessionRenderer();
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        var disabled = false;
        var calls = 0;
        cache.SetService((language, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<SyntaxToken>?>(
                [new SyntaxToken(0, 3, language == "cs" ? "keyword" : "string")]);
        }, language => language != "cs" || !disabled);
        renderer.BindSyntax(cache);
        renderer.RefreshSyntax();
        var log = Field<TranscriptLog>(renderer, "_log");
        log.Add(TranscriptKind.Assistant, "```cs\nlet disabled\n```\n```js\nlet healthy\n```");
        log.Viewport(80, 100, 0);
        await UntilAsync(() => cache.Counts.Entries == 2);
        Assert.True(renderer.RefreshSyntax());
        var before = log.Viewport(80, 100, 0);
        var healthyRow = Assert.Single(before, line => line.Markup.Contains("healthy", StringComparison.Ordinal)).Markup;
        Assert.Contains(Theme.SyntaxString, healthyRow);
        var healthyTokens = cache.LookupTokens("js", "let healthy");
        Assert.NotNull(healthyTokens);
        var bytesBefore = cache.Counts.Bytes;
        disabled = true;

        Assert.True(renderer.RefreshSyntax());
        Assert.Equal(1, cache.Counts.Entries);
        Assert.InRange(cache.Counts.Bytes, 1, bytesBefore - 1);
        Assert.Same(healthyTokens, cache.LookupTokens("js", "let healthy"));
        Assert.Null(cache.LookupTokens("cs", "let disabled"));
        var after = log.Viewport(80, 100, 0);
        Assert.Equal(healthyRow, Assert.Single(after, line => line.Markup.Contains("healthy", StringComparison.Ordinal)).Markup);
        Assert.DoesNotContain(Theme.SyntaxKeyword,
            Assert.Single(after, line => line.Markup.Contains("disabled", StringComparison.Ordinal)).Markup);
        Assert.False(renderer.RefreshSyntax());
        Assert.Equal(0, cache.Counts.Queued);
        Assert.Equal(0, cache.Counts.Active);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetTokens_EnforcesLruEntryAndMemoryBounds()
    {
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        cache.SetService((_, _, _) => Task.FromResult<IReadOnlyList<SyntaxToken>?>(
            [new SyntaxToken(0, 1, "keyword")]), _ => true);
        for (var index = 0; index < TerminalSyntaxCache.MaximumEntries; index++)
        {
            cache.GetTokens("cs", $"item-{index}");
            var count = index + 1;
            await UntilAsync(() => cache.Counts.Entries == count);
        }

        cache.GetTokens("cs", "item-0");
        cache.GetTokens("cs", "extra");
        await UntilAsync(() => cache.Counts.Queued == 0 && cache.Counts.Active == 0);
        Assert.Equal(TerminalSyntaxCache.MaximumEntries, cache.Counts.Entries);
        Assert.NotNull(cache.GetTokens("cs", "item-0"));
        Assert.Equal(0, cache.Counts.Queued);
        // A miss is queued and a worker may drain it within one poll, so absence is observed without scheduling.
        Assert.Null(cache.LookupTokens("cs", "item-1"));
        Assert.True(cache.Counts.Bytes <= TerminalSyntaxCache.MaximumBytes);
    }

    [Fact]
    public async Task GetTokens_LargeEntriesEvictBeforeMemoryLimit()
    {
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        cache.SetService((_, _, _) => Task.FromResult<IReadOnlyList<SyntaxToken>?>(null), _ => true);
        for (var index = 0; index < 80; index++)
        {
            cache.GetTokens("cs", index.ToString("D3") + new string('x', 65000));
            await UntilAsync(() => cache.Counts.Queued == 0 && cache.Counts.Active == 0);
        }

        Assert.InRange(cache.Counts.Entries, 1, 79);
        Assert.InRange(cache.Counts.Bytes, 1, TerminalSyntaxCache.MaximumBytes);
    }

    [Fact]
    public async Task GetTokens_PreservesIndependentPrefixBlocksAndBoundsQueueAndWorkers()
    {
        using var cache = new TerminalSyntaxCache(TimeSpan.FromMilliseconds(150));
        var calls = new ConcurrentQueue<string>();
        var completion = new TaskCompletionSource<IReadOnlyList<SyntaxToken>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.SetService((_, text, _) =>
        {
            calls.Enqueue(text);
            return completion.Task;
        }, _ => true);
        cache.GetTokens("cs", "let");
        cache.GetTokens("cs", "let value");
        Assert.Equal(2, cache.Counts.Queued);
        Assert.Empty(calls);
        await UntilAsync(() => calls.Count == 2);
        Assert.Contains("let", calls);
        Assert.Contains("let value", calls);
        for (var index = 0; index < 100; index++)
        {
            cache.GetTokens("cs", $"distinct-{index}");
        }

        Assert.InRange(cache.Counts.Queued, 1, TerminalSyntaxCache.MaximumQueued);
        await UntilAsync(() => calls.Count == 2);
        Assert.Equal(2, cache.Counts.Active);
        cache.Dispose();
        await cache.Completion.WaitAsync(Timeout);
        completion.SetResult(null);
        Assert.Null(cache.GetTokens("cs", "after close"));
    }

    [Fact]
    public void GetTokens_UnsupportedAliasesNeverEnterQueue()
    {
        using var cache = new TerminalSyntaxCache();
        cache.SetService((_, _, _) => throw new InvalidOperationException("Unexpected tokenization."), _ => false);
        cache.ConsumeRefresh();
        for (var index = 0; index < 100; index++)
        {
            Assert.Null(cache.GetTokens("unknown", index.ToString()));
        }

        Assert.Equal(0, cache.Counts.Queued);
        Assert.False(cache.ConsumeRefresh());
    }

    [Fact]
    public async Task RefreshSyntax_LargeCommittedHistorySettlesAndViewportChangesScheduleWork()
    {
        using var renderer = new SessionRenderer();
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        var calls = 0;
        cache.SetService((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<SyntaxToken>?>([new SyntaxToken(0, 3, "keyword")]);
        }, _ => true);
        renderer.BindSyntax(cache);
        renderer.RefreshSyntax();
        var log = Field<TranscriptLog>(renderer, "_log");
        const int blocks = 160;
        var refreshes = 0;
        for (var index = 0; index < blocks; index++)
        {
            log.Add(TranscriptKind.Assistant, $"```cs\nlet value{index}\n```");
            log.Viewport(80, 20, 0);
            await UntilAsync(() => cache.Counts.Queued == 0 && cache.Counts.Active == 0);
            if (renderer.RefreshSyntax())
            {
                refreshes++;
                log.Viewport(80, 20, 0);
            }
        }

        Assert.Equal(blocks, calls);
        Assert.Equal(blocks, refreshes);
        Assert.Equal(128, cache.Counts.Entries);
        // Force a viewport containing more fences than the cache, then keep polling through completion rebuilds.
        log.Viewport(80, 2000, 0);
        await UntilAsync(() => cache.Counts.Queued == 0 && cache.Counts.Active == 0);
        renderer.RefreshSyntax();
        log.Viewport(80, 2000, 0);
        var settledCalls = calls;
        for (var poll = 0; poll < 30; poll++)
        {
            Assert.False(renderer.RefreshSyntax());
            log.Viewport(80, 2000, 0);
            await Task.Delay(10);
        }

        Assert.Equal(settledCalls, calls);
        Assert.Equal(0, cache.Counts.Queued);
        for (var scroll = 0; scroll <= log.RowCount(79) && cache.Counts.Queued == 0; scroll += 20)
        {
            log.Viewport(79, 20, scroll);
        }

        Assert.True(cache.Counts.Queued > 0);
    }

    [Fact]
    public async Task GetTokens_ContinuousAppendKeepsWorkBoundedAndEventuallyRunsLatestSnapshot()
    {
        using var cache = new TerminalSyntaxCache();
        var calls = new ConcurrentQueue<string>();
        cache.SetService((_, text, _) =>
        {
            calls.Enqueue(text);
            return Task.FromResult<IReadOnlyList<SyntaxToken>?>(null);
        }, _ => true);
        for (var index = 1; index <= 500; index++)
        {
            cache.GetTokens("cs", new string('x', index));
            Assert.InRange(cache.Counts.Queued, 0, 32);
            Assert.InRange(cache.Counts.Active, 0, 2);
        }

        await UntilAsync(() => cache.Counts.Queued == 0 && cache.Counts.Active == 0);
        Assert.Contains(new string('x', 500), calls);
        Assert.InRange(calls.Count, 1, 34);
    }

    [Fact]
    public async Task RefreshSyntax_OneLargeAssistantEntryDoesNotRequeueDroppedFences()
    {
        using var renderer = new SessionRenderer();
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        var completion = new TaskCompletionSource<IReadOnlyList<SyntaxToken>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        cache.SetService((_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return completion.Task;
        }, _ => true);
        renderer.BindSyntax(cache);
        renderer.RefreshSyntax();
        var log = Field<TranscriptLog>(renderer, "_log");
        log.Add(TranscriptKind.Assistant, string.Join("\n", Enumerable.Range(0, 160)
            .Select(index => $"```cs\nlet value{index}\n```")));
        log.Viewport(80, 20, 0);
        Assert.InRange(cache.Counts.Queued, 1, 32);
        completion.SetResult([new SyntaxToken(0, 3, "keyword")]);
        await UntilAsync(() => cache.Counts.Queued == 0 && cache.Counts.Active == 0);
        Assert.True(renderer.RefreshSyntax());
        log.Viewport(80, 20, 0);
        var completedCalls = calls;
        Assert.InRange(completedCalls, 1, 34);
        for (var poll = 0; poll < 30; poll++)
        {
            Assert.False(renderer.RefreshSyntax());
            log.Viewport(80, 20, 0);
            await Task.Delay(10);
        }

        Assert.Equal(completedCalls, calls);
        Assert.Equal(0, cache.Counts.Queued);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshSyntax_UpdatesLiveAndCommittedStylesOnlyWhenConsumed(bool live)
    {
        using var renderer = new SessionRenderer();
        using var cache = new TerminalSyntaxCache(TimeSpan.Zero);
        var completion = new TaskCompletionSource<IReadOnlyList<SyntaxToken>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        cache.SetService((_, _, _) => completion.Task, _ => true);
        renderer.BindSyntax(cache);
        renderer.RefreshSyntax();
        var log = (TranscriptLog)typeof(SessionRenderer)
            .GetField("_log", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer)!;
        const string text = "```cs\nlet value\n```";
        if (live)
        {
            log.AppendLive(TranscriptKind.Assistant, text);
        }
        else
        {
            log.Add(TranscriptKind.Assistant, text);
        }

        var plain = string.Join("\n", log.Viewport(80, 100, 0).Select(line => line.Markup));
        Assert.DoesNotContain($"[{Theme.SyntaxKeyword}]", plain);
        completion.SetResult([new SyntaxToken(0, 3, "keyword")]);
        await UntilAsync(() => cache.Counts.Entries == 1);
        Assert.Equal(plain, string.Join("\n", log.Viewport(80, 100, 0).Select(line => line.Markup)));
        Assert.True(renderer.RefreshSyntax());
        var colored = string.Join("\n", log.Viewport(80, 100, 0).Select(line => line.Markup));
        Assert.Contains(Theme.SyntaxKeyword, colored);
        Assert.NotEqual(plain, colored);
        cache.SetService((_, _, _) => Task.FromResult<IReadOnlyList<SyntaxToken>?>(null), _ => true);
        Assert.True(renderer.RefreshSyntax());
        Assert.Equal(plain, string.Join("\n", log.Viewport(80, 100, 0).Select(line => line.Markup)));
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
}
