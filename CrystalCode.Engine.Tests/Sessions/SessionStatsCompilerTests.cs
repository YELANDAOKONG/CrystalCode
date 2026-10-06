using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionStatsCompilerTests
{
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
        Assert.Equal(210, report.TotalTokens);
        Assert.Equal(105, report.AverageTokensPerSession);
        Assert.Equal(105, report.MedianTokensPerSession);
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
        Assert.Equal(65, report.TotalTokens);
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
