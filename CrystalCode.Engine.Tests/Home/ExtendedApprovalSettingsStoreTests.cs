using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class ExtendedApprovalSettingsStoreTests
{
    private const string SlotProviders =
        """
        {
          "gateway": {
            "protocol": "anthropic",
            "baseUri": "https://example.test/v1/",
            "requiresApiKey": false,
            "models": {
              "judge": { "contextWindow": 200000, "serviceTiers": ["flex", "priority"], "speeds": ["fast"] },
              "plain": { "contextWindow": 200000 }
            }
          }
        }
        """;

    [Fact]
    public void Save_RoundTripsExtendedApprovalAndOmitsDefaults()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        var settings = store.Load().WithExtendedApproval(new ExtendedApprovalSettings(
            new ApprovalModelSettings(
                true,
                "gateway",
                "judge",
                ThinkingSelection.Default,
                "flex",
                "fast"),
            ReviewRiskLevel.Medium,
            includePrimaryReview: false));

        store.Save(settings);
        var loaded = store.Load();
        var text = File.ReadAllText(root.Home.ConfigPath);

        Assert.True(loaded.ExtendedApproval.Enabled);
        Assert.Equal("judge", loaded.ExtendedApproval.Reviewer.Model);
        Assert.Equal("flex", loaded.ExtendedApproval.Reviewer.ServiceTier);
        Assert.Equal("fast", loaded.ExtendedApproval.Reviewer.Speed);
        Assert.Equal(ReviewRiskLevel.Medium, loaded.ExtendedApproval.Trigger);
        Assert.False(loaded.ExtendedApproval.IncludePrimaryReview);
        Assert.Contains("\"extendedApproval\"", text, StringComparison.Ordinal);
        Assert.Contains("\"trigger\": \"medium\"", text, StringComparison.Ordinal);
        Assert.Contains("\"includePrimaryReview\": false", text, StringComparison.Ordinal);

        store.Save(loaded.WithExtendedApproval(new ExtendedApprovalSettings(
            new ApprovalModelSettings(true, "gateway", "judge"))));
        var trimmed = File.ReadAllText(root.Home.ConfigPath);

        Assert.DoesNotContain("trigger", trimmed, StringComparison.Ordinal);
        Assert.DoesNotContain("includePrimaryReview", trimmed, StringComparison.Ordinal);
        Assert.DoesNotContain("serviceTier", trimmed, StringComparison.Ordinal);

        store.Save(loaded.WithExtendedApproval(ExtendedApprovalSettings.Off));
        Assert.DoesNotContain(
            "extendedApproval",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsTheApprovalModelTierAndSpeed()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        var settings = store.Load().WithApprovalModel(new ApprovalModelSettings(
            true,
            "gateway",
            "judge",
            serviceTier: "Priority",
            speed: "fast"));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("priority", loaded.ApprovalModel.ServiceTier);
        Assert.Equal("fast", loaded.ApprovalModel.Speed);
        Assert.Contains(
            "\"serviceTier\": \"priority\"",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        var reviewer = loaded.ForReviewer(loaded.ApprovalModel);
        Assert.Equal("priority", reviewer.ServiceTier);
        Assert.Equal("fast", reviewer.Speed);
    }

    [Fact]
    public void Load_RejectsAnEnabledReviewerTierTheModelDoesNotList()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        store.Save(store.Load());
        WriteConfigField(root, "\"extendedApproval\": { \"enabled\": true, \"provider\": \"gateway\", \"model\": \"plain\", \"serviceTier\": \"flex\" }");

        var exception = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains("Extended approval model service tier", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsAnUnknownTrigger()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        store.Save(store.Load());
        WriteConfigField(root, "\"extendedApproval\": { \"trigger\": \"severe\" }");

        var exception = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Equal("extendedApproval.trigger must be low, medium, or high.", exception.Message);
    }

    [Fact]
    public void Load_RequiresAModelForAnEnabledExtendedApproval()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        store.Save(store.Load());
        WriteConfigField(root, "\"extendedApproval\": { \"enabled\": true }");

        var exception = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Equal("extendedApproval.enabled requires provider and model.", exception.Message);
    }

    private static DiskSettingsStore SlotStore(TemporaryHome root)
    {
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ProvidersPath, SlotProviders);
        return store;
    }

    private static void WriteConfigField(TemporaryHome root, string field)
    {
        var text = File.ReadAllText(root.Home.ConfigPath).TrimEnd();
        var closing = text.LastIndexOf('}');
        File.WriteAllText(root.Home.ConfigPath, text[..closing].TrimEnd() + ",\n  " + field + "\n}");
    }
}
