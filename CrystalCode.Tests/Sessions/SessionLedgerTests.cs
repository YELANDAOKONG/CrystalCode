using Crystal;
using Crystal.Chat;

using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class SessionLedgerTests
{
    [Fact]
    public void Record_KeepsLastUsageSnapshot()
    {
        var ledger = new SessionLedger();
        ledger.Record(Turn(new TokenUsage(100, 10), modelCalls: 1, toolCalls: 2));
        ledger.Record(Turn(new TokenUsage(250, 40), modelCalls: 1, toolCalls: 1));

        Assert.Equal(2, ledger.UserTurns);
        Assert.Equal(2, ledger.ModelCalls);
        Assert.Equal(3, ledger.ToolCalls);
        Assert.Equal(250, ledger.Usage?.InputTokenCount);
        Assert.Equal(40, ledger.Usage?.OutputTokenCount);
        Assert.Equal(350, ledger.CumulativeUsage?.InputTokenCount);
        Assert.Equal(50, ledger.CumulativeUsage?.OutputTokenCount);
    }

    [Fact]
    public void Record_NullUsageKeepsPreviousSnapshot()
    {
        var ledger = new SessionLedger();
        ledger.Record(Turn(new TokenUsage(80, 5)));
        ledger.Record(Turn(null));

        Assert.Equal(80, ledger.Usage?.InputTokenCount);
        Assert.Equal(5, ledger.Usage?.OutputTokenCount);
    }

    [Fact]
    public void Restore_ReplacesCountsAndUsage()
    {
        var ledger = new SessionLedger();
        ledger.Record(Turn(new TokenUsage(10, 1)));
        ledger.Restore(
            4,
            6,
            9,
            new TokenUsage(900, 30, 8),
            new TokenUsage(2_000, 100, 20));

        Assert.Equal(4, ledger.UserTurns);
        Assert.Equal(6, ledger.ModelCalls);
        Assert.Equal(9, ledger.ToolCalls);
        Assert.Equal(900, ledger.Usage?.InputTokenCount);
        Assert.Equal(30, ledger.Usage?.OutputTokenCount);
        Assert.Equal(8, ledger.Usage?.ReasoningTokenCount);
        Assert.Equal(2_000, ledger.CumulativeUsage?.InputTokenCount);
    }

    [Fact]
    public void ReplaceUsage_KeepsCounts()
    {
        var ledger = new SessionLedger();
        ledger.Restore(4, 6, 9, new TokenUsage(900, 30));
        ledger.ReplaceUsage(new TokenUsage(40, 0));

        Assert.Equal(4, ledger.UserTurns);
        Assert.Equal(40, ledger.Usage?.InputTokenCount);
        Assert.Equal(0, ledger.Usage?.OutputTokenCount);
    }

    [Fact]
    public void Record_UsesAccumulatedUsageAcrossModelRounds()
    {
        var ledger = new SessionLedger();
        ledger.Record(
            Turn(
                new TokenUsage(20, 10),
                modelCalls: 2,
                accumulatedUsage: new TokenUsage(30, 15)));

        Assert.Equal(20, ledger.Usage?.InputTokenCount);
        Assert.Equal(30, ledger.CumulativeUsage?.InputTokenCount);
        Assert.Equal(15, ledger.CumulativeUsage?.OutputTokenCount);
    }

    [Fact]
    public void Record_MissingMultiRoundUsageMakesCumulativeUnknown()
    {
        var ledger = new SessionLedger();
        ledger.Record(Turn(new TokenUsage(20, 10), modelCalls: 2));
        ledger.Record(Turn(new TokenUsage(30, 5)));

        Assert.Null(ledger.CumulativeUsage);
    }

    [Fact]
    public void Restore_LegacySessionKeepsCumulativeUsageUnknown()
    {
        var ledger = new SessionLedger();
        ledger.Restore(3, 4, 2, new TokenUsage(100, 10));
        ledger.Record(Turn(new TokenUsage(20, 5)));

        Assert.Null(ledger.CumulativeUsage);
        Assert.Equal(20, ledger.Usage?.InputTokenCount);
    }

    private static TurnResult Turn(
        TokenUsage? usage,
        int modelCalls = 1,
        int toolCalls = 0,
        TokenUsage? accumulatedUsage = null) =>
        new(TurnStopReason.Completed, modelCalls, toolCalls, usage, [], accumulatedUsage);
}
