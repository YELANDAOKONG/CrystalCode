using Crystal.Reasoning;

using CrystalCode.Configuration;

using Xunit;

namespace CrystalCode.Tests.Configuration;

public sealed class ThinkingSelectionTests
{
    [Fact]
    public void ToReasoningOptions_OmitsHintsWhenModelDoesNotThink()
    {
        var model = new ModelSettings(1000);
        var selection = ThinkingSelection.Parse("high");

        Assert.Null(selection.ToReasoningOptions(model));
    }

    [Fact]
    public void ToReasoningOptions_FallsBackToAutomaticWhenEffortIsUnavailable()
    {
        var model = new ModelSettings(
            1000,
            thinking: true,
            thinkingEfforts: ["low", "high", "maximum"]);

        var options = ThinkingSelection.Parse("medium").ToReasoningOptions(model);

        Assert.NotNull(options);
        Assert.Equal(ReasoningMode.Automatic, options.Mode);
        Assert.Null(options.Effort);
    }

    [Fact]
    public void ToReasoningOptions_EnablesAllowedEffort()
    {
        var model = new ModelSettings(
            1000,
            thinking: true,
            thinkingEfforts: ["low", "high", "maximum"]);

        var options = ThinkingSelection.Parse("high").ToReasoningOptions(model);

        Assert.NotNull(options);
        Assert.Equal(ReasoningMode.Enabled, options.Mode);
        Assert.Equal(ReasoningEffort.High, options.Effort);
    }

    [Fact]
    public void Parse_TreatsNoneAsOff()
    {
        Assert.Equal(ThinkingSelection.Off, ThinkingSelection.Parse("none"));
    }

    [Fact]
    public void Parse_TreatsMaxAsMaximum()
    {
        Assert.Equal(new ThinkingSelection("maximum"), ThinkingSelection.Parse("max"));
    }

    [Fact]
    public void ToReasoningOptions_DisablesWhenOff()
    {
        var model = new ModelSettings(1000, thinking: true);

        var options = ThinkingSelection.Off.ToReasoningOptions(model);

        Assert.NotNull(options);
        Assert.Equal(ReasoningMode.Disabled, options.Mode);
        Assert.Null(options.Effort);
    }

    [Fact]
    public void Next_CyclesDefaultOffThenModelEfforts()
    {
        var model = new ModelSettings(
            1000,
            thinking: true,
            thinkingEfforts: ["low", "high"]);

        var first = ThinkingSelection.Next(ThinkingSelection.Default, model);
        var second = ThinkingSelection.Next(first, model);
        var third = ThinkingSelection.Next(second, model);
        var fourth = ThinkingSelection.Next(third, model);

        Assert.Equal(ThinkingSelection.Off, first);
        Assert.Equal(new ThinkingSelection("low"), second);
        Assert.Equal(new ThinkingSelection("high"), third);
        Assert.Equal(ThinkingSelection.Default, fourth);
    }

    [Fact]
    public void Next_StartsAtDefaultWhenCurrentEffortIsUnavailable()
    {
        var model = new ModelSettings(
            1000,
            thinking: true,
            thinkingEfforts: ["low", "high"]);

        var next = ThinkingSelection.Next(ThinkingSelection.Parse("medium"), model);

        Assert.Equal(ThinkingSelection.Default, next);
    }
}
