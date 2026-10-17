using System.Text.Json;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class CatalogSlotTests
{
    [Fact]
    public void Normalize_KeepsDefaultAndRejectsDuplicates()
    {
        var model = new ModelSettings(
            1000,
            serviceTiers: ["Default", "flex", "auto"],
            speeds: ["Fast", "ultrafast"]);

        Assert.Equal(["default", "flex", "auto"], model.ServiceTiers);
        Assert.Equal(["fast", "ultrafast"], model.Speeds);
        Assert.Throws<ArgumentException>(
            () => new ModelSettings(1000, serviceTiers: ["flex", "Flex"]));
        Assert.Throws<ArgumentException>(
            () => new ModelSettings(1000, speeds: ["bad token"]));
    }

    [Fact]
    public void Normalize_AcceptsUnderscoreInTokens()
    {
        var model = new ModelSettings(
            1000,
            serviceTiers: ["auto", "Standard_Only"],
            speeds: ["very_fast"]);

        Assert.Equal(["auto", "standard_only"], model.ServiceTiers);
        Assert.Equal(["very_fast"], model.Speeds);
    }

    [Fact]
    public void ReadProviders_AcceptsAnUnderscoreServiceTier()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "anthropic": {
                "protocol": "anthropic",
                "baseUri": "https://example.test/",
                "models": {
                  "claude-test": {
                    "contextWindow": 1000,
                    "serviceTiers": ["auto", "standard_only"]
                  }
                }
              }
            }
            """);

        var provider = Assert.Single(SettingsMapper.ReadProviders(document.RootElement));

        Assert.Equal(["auto", "standard_only"], provider.Models["claude-test"].ServiceTiers);
    }

    [Fact]
    public void Next_CyclesThroughTokensAndBackToUnset()
    {
        IReadOnlyList<string> tokens = ["flex", "default"];

        Assert.Equal("flex", CatalogSlots.Next(null, tokens));
        Assert.Equal("default", CatalogSlots.Next("flex", tokens));
        Assert.Null(CatalogSlots.Next("default", tokens));
        Assert.Equal("flex", CatalogSlots.Next("retired", tokens));
    }

    [Fact]
    public void Selection_StoresPerModelAndOmitsATokenTheModelNoLongerLists()
    {
        var provider = new ProviderName("local");
        var settings = new HarnessSettings(
            provider,
            "wide",
            CrystalCode.Engine.Approvals.ApprovalMode.Default,
            0.8,
            new ProviderCatalog(
            [
                new ProviderDefinition(
                    provider,
                    ProviderProtocol.OpenAI,
                    new Uri("https://example.test/v1/"),
                    new Dictionary<string, ModelSettings>(StringComparer.Ordinal)
                    {
                        ["wide"] = new(1000, serviceTiers: ["flex", "default"], speeds: ["fast"]),
                        ["narrow"] = new(1000, serviceTiers: ["auto"])
                    })
            ]));

        var selected = settings
            .WithTier(provider, "wide", "Default")
            .WithSpeed(provider, "wide", "fast")
            .WithTier(provider, "narrow", "auto");

        Assert.Equal("default", selected.ServiceTier);
        Assert.Equal("fast", selected.Speed);
        Assert.Equal("auto", selected.WithSelection(provider, "narrow").ServiceTier);
        Assert.Null(selected.WithSelection(provider, "narrow").Speed);

        var stale = selected.WithTier(provider, "wide", null);
        Assert.Null(stale.ServiceTier);
        Assert.Equal("fast", stale.Speed);
        Assert.Throws<ArgumentException>(() => settings.WithSpeed(provider, "narrow", "fast"));
    }

    [Fact]
    public void ReadTokenSelections_NamesTheFieldWhenTheTokenIsBlank()
    {
        using var document = JsonDocument.Parse("""{"openai":{"model":""}}""");
        var exception = Assert.Throws<JsonException>(
            () => SettingsMapper.ReadTokenSelections(document.RootElement, "tierSelections"));
        Assert.Contains("tierSelections", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", exception.Message, StringComparison.Ordinal);
    }
}
