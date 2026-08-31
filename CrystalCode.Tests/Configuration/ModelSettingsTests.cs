using CrystalCode.Configuration;

using Xunit;

namespace CrystalCode.Tests.Configuration;

public sealed class ModelSettingsTests
{
    [Fact]
    public void Constructor_RejectsEffortsWhenThinkingIsDisabled()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ModelSettings(1000, thinkingEfforts: ["high"]));

        Assert.Contains("require thinking", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsEffort_IsFalseWhenOnlyToggleIsConfigured()
    {
        var model = new ModelSettings(1000, thinking: true);

        Assert.False(model.AllowsEffort("high"));
    }

    [Fact]
    public void AllowsEffort_MatchesCrystalNames()
    {
        var model = new ModelSettings(
            1000,
            thinking: true,
            thinkingEfforts: ["Low", "HIGH", "maximum"]);

        Assert.True(model.AllowsEffort("high"));
        Assert.False(model.AllowsEffort("medium"));
        Assert.Equal(["low", "high", "maximum"], model.ThinkingEfforts);
    }

    [Fact]
    public void Constructor_IgnoresNoneAndOffInEffortList()
    {
        var model = new ModelSettings(
            1000,
            thinking: true,
            thinkingEfforts: ["none", "off", "default", "high"]);

        Assert.Equal(["high"], model.ThinkingEfforts);
        Assert.True(model.AllowsEffort("high"));
    }

    [Fact]
    public void Constructor_AcceptsMaxAsMaximum()
    {
        var model = new ModelSettings(
            1000,
            thinking: true,
            thinkingEfforts: ["none", "low", "high", "max"]);

        Assert.Equal(["low", "high", "maximum"], model.ThinkingEfforts);
        Assert.True(model.AllowsEffort("max"));
        Assert.True(model.AllowsEffort("maximum"));
    }
}
