using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Compaction;
using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Engine.Tests.Compaction;

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
    public void ShouldCompact_UsesReportedUsageWhenEstimateIsLow()
    {
        Assert.True(
            ContextAccountant.ShouldCompact(
                estimatedTokens: 10,
                reportedUsage: new TokenUsage(80, 5),
                100,
                0.8));
    }

    [Fact]
    public void ShouldCompact_UsesEstimateWhenReportedUsageIsMissing()
    {
        Assert.True(
            ContextAccountant.ShouldCompact(
                estimatedTokens: 90,
                reportedUsage: null,
                100,
                0.8));
        Assert.False(
            ContextAccountant.ShouldCompact(
                estimatedTokens: 10,
                reportedUsage: null,
                100,
                0.8));
    }

    [Fact]
    public void ShouldCompact_UsesTheAnchorInsteadOfEstimatingTheWholeTranscript()
    {
        var limits = new CompactionLimits(100_000);
        var last = new ChatMessage(ChatRole.Assistant, "done");
        // About 125,000 estimated tokens, but the provider reported 30,000 for the same list.
        ChatItem[] items = [new ChatMessage(ChatRole.User, new string('u', 500_000)), last];

        Assert.True(ContextAccountant.ShouldCompact(items, null, null, limits));
        Assert.False(ContextAccountant.ShouldCompact(items, new ContextAnchor(30_000, last), null, limits));
    }

    [Fact]
    public void ShouldCompact_CountsItemsAddedAfterTheAnchor()
    {
        var limits = new CompactionLimits(100_000);
        var last = new ChatMessage(ChatRole.Assistant, "done");
        var big = new ChatMessage(ChatRole.User, new string('t', 200_000));
        ChatItem[] items = [last, big];

        Assert.True(ContextAccountant.ShouldCompact(items, new ContextAnchor(30_000, last), null, limits));
    }

    [Fact]
    public void ShouldCompact_FallsBackToEstimateAndReportedUsageWithoutAnAnchor()
    {
        var limits = new CompactionLimits(100);
        ChatItem[] items = [new ChatMessage(ChatRole.User, "hi")];

        Assert.True(ContextAccountant.ShouldCompact(items, null, new TokenUsage(80, 5), limits));
        Assert.False(ContextAccountant.ShouldCompact(items, null, new TokenUsage(10, 1), limits));
    }

    [Fact]
    public void ShouldCompact_IgnoresAStaleReportedUsageWhileAnAnchorHolds()
    {
        var limits = new CompactionLimits(100_000);
        var last = new ChatMessage(ChatRole.Assistant, "done");
        ChatItem[] items = [last];

        Assert.False(ContextAccountant.ShouldCompact(
            items,
            new ContextAnchor(30_000, last),
            new TokenUsage(90_000, 0),
            limits));
    }

    [Fact]
    public void CompactLimit_AppliesTheThresholdAndTheReservation()
    {
        Assert.Equal(160_000, ContextAccountant.CompactLimit(200_000, 0.8));
        Assert.Equal(24_000, ContextAccountant.CompactLimit(32_000, 0.8, maxTokens: 32_000));
        // A tiny window keeps three quarters for input instead of reserving all of it.
        Assert.Equal(75, ContextAccountant.CompactLimit(100, 0.8));
        Assert.Equal(25_600, ContextAccountant.CompactLimit(32_000, 0.8, reserved: 4_000));
        Assert.Equal(2_000, ContextAccountant.CompactLimit(32_000, 0.8, reserved: 30_000));
        // The 0.8 fraction is tighter than either reservation, so both leave the same ceiling.
        Assert.Equal(
            ContextAccountant.CompactLimit(200_000, 0.8, reserved: 20_000),
            ContextAccountant.CompactLimit(200_000, 0.8, reserved: 30_000));
    }

    [Fact]
    public void ShouldCompact_HonorsADeclaredReservation()
    {
        Assert.True(ContextAccountant.ShouldCompact(25_000, 32_000, 0.8));
        Assert.False(ContextAccountant.ShouldCompact(25_000, 32_000, 0.8, reserved: 4_000));
    }

    [Fact]
    public void OutputTokens_LeavesTheRestOfTheWindowForTheReply()
    {
        // A model whose output cap equals its window can still be asked for a
        // reply that fits once the prompt is capped at the compact limit.
        Assert.Equal(8_192, ContextAccountant.OutputTokens(32_768, 0.8, 32_768));
        Assert.Equal(200_000, ContextAccountant.OutputTokens(1_000_000, 0.8, 1_000_000));
        Assert.Equal(32_000, ContextAccountant.OutputTokens(200_000, 0.8, 32_000));
        Assert.Equal(40_000, ContextAccountant.OutputTokens(200_000, 0.8, 128_000));
        Assert.Equal(0, ContextAccountant.OutputTokens(200_000, 0.8, maxTokens: null));
    }
}
