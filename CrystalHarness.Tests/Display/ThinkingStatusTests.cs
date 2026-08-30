using CrystalHarness.Configuration;
using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class ThinkingStatusTests
{
    [Fact]
    public void For_IsEmptyWhenModelDoesNotThink()
    {
        var text = ThinkingStatus.For(new ModelSettings(1000), ThinkingSelection.Parse("high"));

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void For_ShowsOffWhenThinkingIsDisabled()
    {
        var model = new ModelSettings(1000, thinking: true, thinkingEfforts: ["low", "high"]);

        Assert.Equal("Think Off", ThinkingStatus.For(model, ThinkingSelection.Off));
    }

    [Fact]
    public void For_ShowsDefaultWhenThinkingUsesProviderDefault()
    {
        var model = new ModelSettings(1000, thinking: true, thinkingEfforts: ["low", "high"]);

        Assert.Equal("Think Default", ThinkingStatus.For(model, ThinkingSelection.Default));
    }

    [Fact]
    public void For_ShowsEffortWhenThinkingIsOn()
    {
        var model = new ModelSettings(1000, thinking: true, thinkingEfforts: ["low", "high", "maximum"]);

        Assert.Equal("Think High", ThinkingStatus.For(model, ThinkingSelection.Parse("high")));
        Assert.Equal("Think Maximum", ThinkingStatus.For(model, ThinkingSelection.Parse("max")));
    }

    [Fact]
    public void For_ShowsDefaultWhenStoredEffortIsUnavailable()
    {
        var model = new ModelSettings(1000, thinking: true, thinkingEfforts: ["low", "high"]);

        Assert.Equal("Think Default", ThinkingStatus.For(model, ThinkingSelection.Parse("medium")));
    }
}
