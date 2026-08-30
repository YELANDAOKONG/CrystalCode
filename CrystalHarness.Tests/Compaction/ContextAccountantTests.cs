using Crystal;

using CrystalHarness.Compaction;
using CrystalHarness.Configuration;

using Xunit;

namespace CrystalHarness.Tests.Compaction;

public sealed class ContextAccountantTests
{
    [Fact]
    public void ShouldCompact_WhenUsageCrossesThreshold()
    {
        var usage = new TokenUsage(80, 5);

        Assert.True(
            ContextAccountant.ShouldCompact(
                usage,
                100,
                HarnessSettings.DefaultCompactionThreshold));
    }

    [Fact]
    public void ShouldCompact_IsFalseWhenUsageIsMissing()
    {
        Assert.False(
            ContextAccountant.ShouldCompact(
                null,
                100,
                HarnessSettings.DefaultCompactionThreshold));
    }

    [Fact]
    public void ShouldCompact_IsFalseBelowThreshold()
    {
        Assert.False(ContextAccountant.ShouldCompact(new TokenUsage(10, 1), 100, 0.8));
    }

    [Fact]
    public void CompactLimit_UsesReservedOutputOnLargeWindows()
    {
        Assert.Equal(80, ContextAccountant.CompactLimit(100, 0.8));
        Assert.Equal(12_000, ContextAccountant.CompactLimit(32_000, 0.8, maxTokens: 20_000));
    }
}
