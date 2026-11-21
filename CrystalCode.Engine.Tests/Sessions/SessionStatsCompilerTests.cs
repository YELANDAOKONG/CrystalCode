using System.Collections.Immutable;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Plugins.Clients;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionStatsCompilerTests
{
    [Fact]
    public async Task Stats_PreservesCacheUsageOnResumeAndCountsForkRequestsOnce()
    {
        ChatStreamEvent[] reply =
        [
            new ChatTextDelta(0, 0, ChatRole.Assistant, "done"),
            new ChatCandidateCompleted(0, FinishReason.Stop)
        ];
        var requests = new Queue<ProviderRequestUsage>(
        [
            new ProviderRequestUsage(1, InputTokens: 100, CacheReadTokens: 60, CacheWriteTokens: 5)
            {
                CacheWritesByLifetime = ImmutableDictionary<int, long>.Empty.Add(300, 5)
            },
            new ProviderRequestUsage(1, InputTokens: 200, CacheWriteTokens: 0),
            new ProviderRequestUsage(1, InputTokens: 100, CacheReadTokens: 0),
            new ProviderRequestUsage(1, InputTokens: 50, CacheReadTokens: 25)
        ]);
        void Report(ProviderRequestContext context) => context.ReportUsage(requests.Dequeue());

        using var original = new HeadlessSession(new ScriptedStreamingClient(reply, reply), observeRequest: Report);
        await original.Session.StartAsync(CancellationToken.None);
        await original.RunTurnAsync("first");
        await original.RunTurnAsync("second");
        var stored = original.ReadSaved();
        Assert.Equal(2, stored.CacheUsage!.RequestCount);
        Assert.Equal(60, stored.CacheUsage.ReadTokens);

        using var resumed = new HeadlessSession(new ScriptedStreamingClient(reply, reply), stored, observeRequest: Report);
        await resumed.Session.StartAsync(CancellationToken.None);
        await resumed.RunTurnAsync("resume");
        Assert.Equal(3, resumed.ReadSaved().CacheUsage!.RequestCount);
        await resumed.Session.SubmitAsync("/fork", CancellationToken.None);
        Assert.Null(resumed.ReadSaved().CacheUsage);
        await resumed.RunTurnAsync("branch");
        Assert.Equal(25, resumed.ReadSaved().CacheUsage!.ReadTokens);

        await resumed.Session.SubmitAsync("/stats", CancellationToken.None);
        var report = Assert.Single(resumed.Observer.Events.OfType<StatsReported>()).Report;
        var cache = Assert.IsType<SessionCacheUsage>(report.CacheUsage);
        Assert.Equal(2, report.CacheTrackedSessions);
        Assert.Equal(4, cache.RequestCount);
        Assert.Equal(85, cache.ReadTokens);
        Assert.Equal(5, cache.WriteTokens);
        Assert.Equal(3, cache.ReadReports);
        Assert.Equal(2, cache.WriteReports);
        Assert.Equal(250, cache.ComparableInputTokens);
        Assert.Equal(34m, report.CacheReadRatePercent);
        Assert.Equal(new ProviderCacheWriteUsage(300, 5, 1), Assert.Single(cache.WritesByLifetime));
    }

    [Fact]
    public void Compile_KeepsLegacyCacheUsageUnknownAndAppliesTheDayWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var legacy = new SessionDocument { UpdatedUtc = now };
        var outsideWindow = new SessionDocument
        {
            UpdatedUtc = now.AddDays(-8),
            CacheUsage = new SessionCacheUsage { RequestCount = 1, ReadReports = 1, ReadTokens = 900 }
        };
        var report = SessionStatsCompiler.Compile([legacy, outsideWindow], now, windowDays: 7, topTools: 5);

        Assert.Equal(1, report.Sessions);
        Assert.Equal(0, report.CacheTrackedSessions);
        Assert.Null(report.CacheUsage);
        Assert.Null(report.CacheReadRatePercent);
        Assert.Contains(("Cache reads", "--"), StatsText.CacheRows(report));
    }

    [Fact]
    public void NextTurn_TracksLateUsageInTheOriginalTurnAndSession()
    {
        var original = new ProviderUsageLedger("session");
        var next = original.NextTurn();
        original.Record("Auxiliary", "provider", "model", new ProviderRequestUsage(1, CacheReadTokens: 0));

        Assert.Empty(next.Snapshot());
        Assert.Equal(0, Assert.Single(original.Snapshot()).CacheReadTokens);
        Assert.Equal(0, next.CacheSnapshot().Usage!.ReadTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SideCacheUsage_SavesBeforeClearingOrResuming(bool resume)
    {
        ChatStreamEvent[] reply =
        [
            new ChatTextDelta(0, 0, ChatRole.Assistant, "done"),
            new ChatCandidateCompleted(0, FinishReason.Stop)
        ];
        using var session = new HeadlessSession(new ScriptedStreamingClient(reply, reply),
            observeRequest: context => context.ReportUsage(new ProviderRequestUsage(1, InputTokens: 100, CacheReadTokens: 60)));
        await session.Session.StartAsync(CancellationToken.None);
        await session.RunTurnAsync("first");
        var source = session.ReadSaved();
        await session.Session.SubmitAsync("/btw side", CancellationToken.None);
        await session.Session.SideQuestionTask;
        Assert.Equal(1, session.ReadSaved().CacheUsage!.RequestCount);

        var store = new SessionStore(session.Home);
        var command = "/clear";
        if (resume)
        {
            var destination = new SessionDocument
            {
                Id = SessionStore.NewId(),
                Workspace = source.Workspace,
                Items = source.Items
            };
            store.Save(destination);
            command = $"/resume {destination.Id}";
        }

        await session.Session.SubmitAsync(command, CancellationToken.None);

        Assert.NotEqual(source.Id, session.Session.SessionId);
        Assert.True(store.TryLoad(source.Id!, out var saved));
        Assert.Equal(2, saved.CacheUsage!.RequestCount);
        Assert.Equal(120, saved.CacheUsage.ReadTokens);
        Assert.Equal(2, saved.CacheUsageSamples!.Count);
    }

    [Fact]
    public void Compile_DateFiltersCacheRequestsAcrossTheSelectedWorkspaceSessions()
    {
        var now = new DateTimeOffset(2026, 11, 21, 5, 0, 0, TimeSpan.Zero);
        var cutoff = now.AddDays(-7);
        var session = new SessionDocument
        {
            UpdatedUtc = now,
            CacheUsage = new SessionCacheUsage { RequestCount = 3, ReadTokens = 100 },
            CacheUsageSamples =
            [
                new(cutoff.AddTicks(-1), 100, 80, null, null),
                new(cutoff, 50, 20, null, null),
                new(now.AddTicks(1), 100, 25, null, null)
            ]
        };
        var undated = new SessionDocument
        {
            UpdatedUtc = now,
            CacheUsage = new SessionCacheUsage { RequestCount = 1, ReadTokens = 40 }
        };
        var report = SessionStatsCompiler.Compile([session, undated], now, windowDays: 7, topTools: 5);

        Assert.Equal(1, report.CacheTrackedSessions);
        Assert.Equal(7, report.CacheWindowDays);
        Assert.Equal(20, report.CacheUsage!.ReadTokens);
        Assert.Equal(40m, report.CacheReadRatePercent);
        var all = SessionStatsCompiler.Compile([session, undated], now, windowDays: null, topTools: 5);
        Assert.Equal(140, all.CacheUsage!.ReadTokens);
    }

    [Fact]
    public void Compile_UsesOnlyKnownSessionTotalsAndPreservesLegacySingleCalls()
    {
        var sessions = new[]
        {
            new SessionDocument
            {
                ModelCalls = 2,
                CumulativeUsage = new SessionUsageDocument { InputTokenCount = 100, OutputTokenCount = 50, ReasoningTokenCount = 10 }
            },
            new SessionDocument
            {
                ModelCalls = 2,
                Usage = new SessionUsageDocument { InputTokenCount = 900, OutputTokenCount = 100, ReasoningTokenCount = 40 }
            },
            new SessionDocument
            {
                ModelCalls = 1,
                Usage = new SessionUsageDocument { InputTokenCount = 10, OutputTokenCount = 20 }
            }
        };

        var report = SessionStatsCompiler.Compile(sessions, DateTimeOffset.UtcNow, windowDays: null, topTools: 5);

        Assert.Equal(2, report.TokenTrackedSessions);
        Assert.Equal(1, report.ReasoningTrackedSessions);
        Assert.Equal(110, report.InputTokens);
        Assert.Equal(70, report.OutputTokens);
        Assert.Equal(10, report.ReasoningTokens);
        Assert.Equal(180, report.TotalTokens);
        Assert.Equal(90, report.AverageTokensPerSession);
        Assert.Equal(90, report.MedianTokensPerSession);
        var unknown = SessionStatsCompiler.Compile([sessions[1]], DateTimeOffset.UtcNow, null, 5);
        Assert.Null(unknown.TotalTokens);
        Assert.Null(unknown.AverageTokensPerSession);
    }

    [Fact]
    public void Compile_AggregatesTokensAndTopTools()
    {
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        var sessions = new[]
        {
            Session(
                created: now.AddDays(-2),
                updated: now.AddDays(-1),
                userTurns: 3,
                modelCalls: 4,
                toolCalls: 5,
                input: 100,
                output: 50,
                reasoning: 10,
                tools: ["read", "bash", "read"]),
            Session(
                created: now.AddDays(-10),
                updated: now.AddDays(-9),
                userTurns: 1,
                modelCalls: 1,
                toolCalls: 1,
                input: 30,
                output: 20,
                reasoning: 0,
                tools: ["glob"])
        };

        var report = SessionStatsCompiler.Compile(sessions, now, windowDays: null, topTools: 2);

        Assert.Equal(2, report.Sessions);
        Assert.Equal(4, report.UserTurns);
        Assert.Equal(5, report.ModelCalls);
        Assert.Equal(6, report.ToolCalls);
        Assert.Equal(130, report.InputTokens);
        Assert.Equal(70, report.OutputTokens);
        Assert.Equal(10, report.ReasoningTokens);
        Assert.Equal(200, report.TotalTokens);
        Assert.Equal(100, report.AverageTokensPerSession);
        Assert.Equal(100, report.MedianTokensPerSession);
        Assert.Equal("read", report.TopTools[0].Name);
        Assert.Equal(2, report.TopTools[0].Count);
        Assert.Equal("bash", report.TopTools[1].Name);
    }

    [Fact]
    public void Compile_RespectsDayWindow()
    {
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        var report = SessionStatsCompiler.Compile(
            [
                Session(
                    created: now.AddDays(-1),
                    updated: now.AddHours(-3),
                    userTurns: 2,
                    modelCalls: 3,
                    toolCalls: 2,
                    input: 40,
                    output: 20,
                    reasoning: 5,
                    tools: ["read"]),
                Session(
                    created: now.AddDays(-20),
                    updated: now.AddDays(-20),
                    userTurns: 9,
                    modelCalls: 9,
                    toolCalls: 9,
                    input: 900,
                    output: 900,
                    reasoning: 900,
                    tools: ["bash"])
            ],
            now,
            windowDays: 7,
            topTools: 5);

        Assert.Equal(1, report.Sessions);
        Assert.Equal(60, report.TotalTokens);
        Assert.Equal(7, report.WindowDays);
        Assert.Single(report.TopTools);
        Assert.Equal("read", report.TopTools[0].Name);
    }

    [Fact]
    public void Compile_CountsToolsFromTheArchiveWhenTheModelContextIsCompacted()
    {
        var now = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        var session = Session(
            created: now.AddDays(-1),
            updated: now,
            userTurns: 4,
            modelCalls: 6,
            toolCalls: 3,
            input: 20,
            output: 10,
            reasoning: 0,
            tools: ["read"]);
        session.Archive =
        [
            Tool("bash"),
            Tool("read"),
            Tool("glob")
        ];

        var report = SessionStatsCompiler.Compile([session], now, windowDays: null, topTools: 5);

        Assert.Equal(3, report.ToolCalls);
        Assert.Equal(["bash", "glob", "read"], report.TopTools.Select(static tool => tool.Name));
        Assert.All(report.TopTools, static tool => Assert.Equal(1, tool.Count));
    }

    private static SessionDocument Session(
        DateTimeOffset created,
        DateTimeOffset updated,
        int userTurns,
        int modelCalls,
        int toolCalls,
        long input,
        long output,
        long reasoning,
        IReadOnlyList<string> tools) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Workspace = "/work",
            CreatedUtc = created,
            UpdatedUtc = updated,
            UserTurns = userTurns,
            ModelCalls = modelCalls,
            ToolCalls = toolCalls,
            CumulativeUsage = new SessionUsageDocument
            {
                InputTokenCount = input,
                OutputTokenCount = output,
                ReasoningTokenCount = reasoning
            },
            Items =
            [
                .. tools.Select(tool => new SessionItemDocument
                {
                    Kind = "tool_call",
                    Name = tool,
                    CallId = Guid.NewGuid().ToString("N"),
                    Arguments = "{}"
                })
            ]
        };

    private static SessionItemDocument Tool(string name) =>
        new()
        {
            Kind = "tool_call",
            Name = name,
            CallId = Guid.NewGuid().ToString("N"),
            Arguments = "{}"
        };
}
