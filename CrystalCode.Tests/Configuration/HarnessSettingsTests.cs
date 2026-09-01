using CrystalCode.Approvals;
using CrystalCode.Configuration;

using Xunit;

namespace CrystalCode.Tests.Configuration;

public sealed class HarnessSettingsTests
{
    [Fact]
    public void WithSelection_SwitchesProviderAndModel()
    {
        var catalog = ModelSelectionTests.Catalog();
        var settings = new HarnessSettings(
            ProviderName.DeepSeek,
            "deepseek-v4-flash",
            ApprovalMode.Default,
            0.8,
            catalog);

        var next = settings.WithSelection(new ProviderName("openrouter"), "anthropic/claude-sonnet-4");

        Assert.Equal("openrouter", next.Provider.Value);
        Assert.Equal("anthropic/claude-sonnet-4", next.Model);
        Assert.Equal(200000, next.ActiveModel.ContextWindow);
        Assert.Equal(ProviderName.DeepSeek, settings.Provider);
    }

    [Fact]
    public void WithEstimatedTokens_SetsHostFlag()
    {
        var catalog = ModelSelectionTests.Catalog();
        var settings = new HarnessSettings(
            ProviderName.DeepSeek,
            "deepseek-v4-flash",
            ApprovalMode.Default,
            0.8,
            catalog);

        var next = settings.WithEstimatedTokens(true);

        Assert.True(next.EstimatedTokens);
        Assert.False(settings.EstimatedTokens);
    }
}
