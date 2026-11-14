using System.Diagnostics;

using CrystalCode.Engine.Plugins;
using CrystalCode.Plugins.Syntax;
using CrystalCode.Plugins.Syntax.Interfaces;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class SyntaxServiceTests
{
    [Fact]
    public async Task TokenizeAsync_MapsAllKindsAndSnapshotsLanguages()
    {
        var tokenizer = new Tokenizer((_, _) => Task.FromResult<IReadOnlyList<PluginSyntaxToken>>(
            Enum.GetValues<PluginSyntaxKind>().Select((kind, index) => new PluginSyntaxToken(index, 1, kind)).ToArray()));
        var service = Create(tokenizer);
        tokenizer.ThrowLanguages = true;

        Assert.True(service.Supports("C#"));
        var result = await service.TokenizeAsync("C#", "abcdefgh", CancellationToken.None);

        Assert.Equal(["keyword", "string", "number", "comment", "type", "function", "operator", "punctuation"],
            result!.Select(token => token.Kind));
    }

    [Theory]
    [InlineData(-1, 1, "abc")]
    [InlineData(0, 0, "abc")]
    [InlineData(0, -1, "abc")]
    [InlineData(2, 2, "abc")]
    [InlineData(0, int.MaxValue, "abc")]
    [InlineData(1, 1, "\ud83d\ude00")]
    [InlineData(0, 1, "\ud83d\ude00")]
    public async Task TokenizeAsync_InvalidSpanDisablesTokenizer(int start, int length, string text)
    {
        var notes = new List<string>();
        var service = Create(new Tokenizer((_, _) => Task.FromResult<IReadOnlyList<PluginSyntaxToken>>(
            [new(start, length, PluginSyntaxKind.Keyword)])), notes: notes);

        Assert.Null(await service.TokenizeAsync("c#", text, CancellationToken.None));
        Assert.False(service.Supports("c#"));
        Assert.Single(notes);
    }

    [Fact]
    public async Task TokenizeAsync_RejectsOverlapOrderUnknownKindsAndExcessTokens()
    {
        IReadOnlyList<PluginSyntaxToken>[] invalid =
        [
            [new(0, 2, PluginSyntaxKind.Keyword), new(1, 1, PluginSyntaxKind.String)],
            [new(1, 1, PluginSyntaxKind.Keyword), new(0, 1, PluginSyntaxKind.String)],
            [new(0, 1, (PluginSyntaxKind)999)],
            Enumerable.Repeat(new PluginSyntaxToken(0, 1, PluginSyntaxKind.Keyword), 16385).ToArray(),
            null!
        ];
        foreach (var tokens in invalid)
        {
            var service = Create(new Tokenizer((_, _) => Task.FromResult(tokens)));
            Assert.Null(await service.TokenizeAsync("c#", "abc", CancellationToken.None));
            Assert.False(service.Supports("c#"));
        }
    }

    [Fact]
    public async Task TokenizeAsync_InputLimitSkipsPluginAndExactLimitsSucceed()
    {
        var calls = 0;
        var service = Create(new Tokenizer((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<PluginSyntaxToken>>(Enumerable.Range(0, 16384)
                .Select(index => new PluginSyntaxToken(index, 1, PluginSyntaxKind.Number)).ToArray());
        }));
        Assert.Null(await service.TokenizeAsync("c#", new string('a', 65537), CancellationToken.None));
        Assert.Equal(0, calls);
        Assert.Equal(16384, (await service.TokenizeAsync("c#", new string('a', 65536), CancellationToken.None))!.Count);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task TokenizeAsync_BlockingEntryTimesOutAndKeepsPermitAcrossReplacement()
    {
        using var release = new ManualResetEventSlim();
        using var limiter = new SemaphoreSlim(2, 2);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notes = new List<string>();
        var tokenizer = new Tokenizer((_, _) =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(10));
            throw new InvalidOperationException("private source code");
        });
        try
        {
            var first = Create(tokenizer, limiter, notes);
            var clock = Stopwatch.StartNew();
            var pending = first.TokenizeAsync("c#", "abc", CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3));
            Assert.False(first.Supports("c#"));
            first.Retire();
            var second = Create(tokenizer, limiter, notes);
            Assert.Null(await second.TokenizeAsync("c#", "abc", CancellationToken.None));
            Assert.Equal(0, limiter.CurrentCount);
            second.Retire();
            var calls = 0;
            var third = Create(new Tokenizer((_, _) =>
            {
                calls++;
                return Task.FromResult<IReadOnlyList<PluginSyntaxToken>>([]);
            }), limiter);
            Assert.Null(await third.TokenizeAsync("c#", "abc", CancellationToken.None));
            Assert.Equal(0, calls);
            Assert.All(notes, note => Assert.DoesNotContain("private source code", note));
        }
        finally
        {
            release.Set();
            // Wait for actual completion before disposing the shared semaphore.
            var deadline = Stopwatch.StartNew();
            while (limiter.CurrentCount < 2 && deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(10);
            }
        }

        Assert.Equal(2, limiter.CurrentCount);
    }

    [Fact]
    public async Task TokenizeAsync_CancellationAndRetirementDiscardLateResults()
    {
        foreach (var retire in new[] { false, true })
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = new TaskCompletionSource<IReadOnlyList<PluginSyntaxToken>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var notes = new List<string>();
            var service = Create(new Tokenizer((_, _) =>
            {
                entered.TrySetResult();
                return result.Task;
            }), notes: notes);
            using var cancellation = new CancellationTokenSource();
            var pending = service.TokenizeAsync("c#", "abc", cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (retire)
            {
                service.Retire();
                Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.False(service.Supports("c#"));
            }
            else
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
                Assert.True(service.Supports("c#"));
            }

            result.SetResult([new(0, 3, PluginSyntaxKind.Keyword)]);
            Assert.Empty(notes);
        }
    }

    [Fact]
    public async Task TokenizeAsync_ExceptionDisablesAllAliasesWithoutLeakingText()
    {
        var notes = new List<string>();
        var service = Create(new Tokenizer((_, _) => throw new Exception("secret")), notes: notes);
        Assert.Null(await service.TokenizeAsync("c#", "abc", CancellationToken.None));
        Assert.False(service.Supports("cs"));
        Assert.DoesNotContain("secret", Assert.Single(notes));
    }

    [Fact]
    public void Admission_FirstRegistrationWinsCaseInsensitiveAndRejectsInvalidAliases()
    {
        var registrations = new List<PluginTokenizerRegistration>();
        var notes = new List<string>();
        var first = new Tokenizer((_, _) => Task.FromResult<IReadOnlyList<PluginSyntaxToken>>([]));
        PluginTokenizerAdmission.Add("First", [first], registrations, notes);
        PluginTokenizerAdmission.Add("Second", [first], registrations, notes);
        Assert.Same(first, Assert.Single(registrations).Tokenizer);
        Assert.Equal(2, notes.Count);
        foreach (var alias in new[] { "", "1abc", "c sharp", "a.b", "é", new string('a', 65) })
        {
            PluginTokenizerAdmission.Add("Invalid", [new Tokenizer((_, _) => throw new Exception()) { Aliases = [alias] }], registrations, notes);
        }

        Assert.Single(registrations);
    }

    private static SyntaxService Create(Tokenizer tokenizer, SemaphoreSlim? limiter = null, List<string>? notes = null)
    {
        var registrations = new List<PluginTokenizerRegistration>();
        PluginTokenizerAdmission.Add("Fixture", [tokenizer], registrations, notes ?? []);
        return new SyntaxService(registrations, limiter ?? new SemaphoreSlim(2, 2), note => notes?.Add(note));
    }

    private sealed class Tokenizer(Func<string, CancellationToken, Task<IReadOnlyList<PluginSyntaxToken>>> tokenize) : IPluginTokenizer
    {
        public bool ThrowLanguages { get; set; }

        public IReadOnlyList<string> Aliases { get; init; } = ["C#", "cs"];

        public IReadOnlyList<string> Languages => ThrowLanguages ? throw new Exception("Must not read again") : Aliases;

        public Task<IReadOnlyList<PluginSyntaxToken>> TokenizeAsync(string language, string text, CancellationToken cancellationToken) =>
            tokenize(language, cancellationToken);
    }
}
