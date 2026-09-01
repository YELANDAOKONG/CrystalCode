using Crystal;

using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class UsageAccumulatorTests
{
    [Fact]
    public void Last_IsTheLatestRound_NotTheSum()
    {
        var usage = new UsageAccumulator();
        usage.Add(new TokenUsage(10, 5, 2));
        usage.Add(new TokenUsage(20, 10, 4));

        Assert.Equal(20, usage.Last?.InputTokenCount);
        Assert.Equal(10, usage.Last?.OutputTokenCount);
        Assert.Equal(4, usage.Last?.ReasoningTokenCount);
        Assert.Equal(30, usage.Build()?.InputTokenCount);
        Assert.Equal(15, usage.Build()?.OutputTokenCount);
    }

    [Fact]
    public void Last_KeepsPreviousSnapshotWhenARoundOmitsUsage()
    {
        var usage = new UsageAccumulator();
        usage.Add(new TokenUsage(80, 5));
        usage.Add(null);

        Assert.Equal(80, usage.Last?.InputTokenCount);
        Assert.Null(usage.Build());
    }
}
