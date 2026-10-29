using CrystalCode.Engine.Configuration;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class BridgeHostSettingsTests
{
    [Fact]
    public void Default_OffersTheBuiltInFiveAndIsNotCustom()
    {
        var settings = BridgeHostSettings.Default;

        Assert.False(settings.Custom);
        Assert.True(settings.IsDefault);
        Assert.Equal(["mode", "model", "thinking", "approval", "agents"], settings.Effective);
    }

    [Fact]
    public void ASavedListIsIgnoredWhileCustomIsOff()
    {
        var settings = new BridgeHostSettings(false, ["mode"]);

        Assert.False(settings.IsDefault);
        Assert.Equal(BridgeHostSettings.DefaultOptions, settings.Effective);
    }

    [Fact]
    public void ACustomListIsOfferedAsWrittenWithoutDuplicates()
    {
        var settings = new BridgeHostSettings(true, ["Model", "mode", "model"]);

        Assert.Equal(["model", "mode"], settings.Effective);
    }

    [Fact]
    public void All_ExpandsToEveryOption()
    {
        var settings = new BridgeHostSettings(true, ["all"]);

        Assert.Equal(BridgeHostSettings.AvailableOptions, settings.Effective);
        Assert.Equal(26, settings.Effective.Count);
    }

    [Fact]
    public void AvailableOptions_AreTheEngineCatalogIds()
    {
        Assert.Equal(
            ["mode", "model", "thinking", "context", "tier", "speed", "approval", "approval_model",
                "approval_thinking", "approval_tier", "approval_speed", "approval_context", "extended_approval",
                "extended_thinking", "extended_tier", "extended_speed", "extended_context", "prompt_set", "agents",
                "compaction_model", "compaction_thinking", "compaction_tier", "compaction_speed", "compaction_context",
                "micro_compaction", "estimated_tokens"],
            BridgeHostSettings.AvailableOptions);
        Assert.All(BridgeHostSettings.DefaultOptions, id => Assert.Contains(id, BridgeHostSettings.AvailableOptions));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("mode,nope")]
    public void Constructor_RejectsUnknownIds(string entry)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new BridgeHostSettings(true, [.. entry.Split(',')]));

        Assert.Contains("Unknown config option", exception.Message, StringComparison.Ordinal);
        Assert.Contains("nope", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsABlankEntry()
    {
        var exception = Assert.Throws<ArgumentException>(() => new BridgeHostSettings(true, [" "]));

        Assert.Contains("cannot be empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsAnEmptyList()
    {
        var exception = Assert.Throws<ArgumentException>(() => new BridgeHostSettings(true, []));

        Assert.Contains("at least one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_ReadsACommaSeparatedListAndReportsBadIds()
    {
        Assert.True(BridgeHostSettings.TryParse("mode, approval ,model", out var options, out _));
        Assert.Equal(["mode", "approval", "model"], options);

        Assert.False(BridgeHostSettings.TryParse("mode,nope", out _, out var error));
        Assert.Contains("nope", error, StringComparison.Ordinal);

        Assert.False(BridgeHostSettings.TryParse("", out _, out error));
        Assert.NotEmpty(error);
    }
}
