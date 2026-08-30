using Crystal;
using Crystal.Chat;

using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

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
        ledger.Restore(4, 6, 9, new TokenUsage(900, 30, 8));

        Assert.Equal(4, ledger.UserTurns);
        Assert.Equal(6, ledger.ModelCalls);
        Assert.Equal(9, ledger.ToolCalls);
        Assert.Equal(900, ledger.Usage?.InputTokenCount);
        Assert.Equal(30, ledger.Usage?.OutputTokenCount);
        Assert.Equal(8, ledger.Usage?.ReasoningTokenCount);
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

    private static TurnResult Turn(TokenUsage? usage, int modelCalls = 1, int toolCalls = 0) =>
        new(TurnStopReason.Completed, modelCalls, toolCalls, usage, []);
}
