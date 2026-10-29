using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class SideModelSlotStoreTests
{
    private const string SlotProviders =
        """
        {
          "gateway": {
            "protocol": "anthropic",
            "baseUri": "https://example.test/v1/",
            "requiresApiKey": false,
            "models": {
              "judge": {
                "contextWindow": 200000,
                "imageInput": true,
                "contextWindows": [100000, 200000],
                "serviceTiers": ["flex", "priority"],
                "speeds": ["fast"]
              },
              "plain": { "contextWindow": 200000, "imageInput": true }
            }
          }
        }
        """;

    [Fact]
    public void Save_RoundTripsImageAndCompactionTierAndSpeedAndOmitsUnset()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        var settings = store.Load()
            .WithResourceModel(ResourceKind.Image, new ResourceModelSettings(true, "gateway", "judge")
                .WithServiceTier("priority")
                .WithSpeed("fast"))
            .WithCompactionModel(new CompactionModelSettings(true, "gateway", "judge")
                .WithServiceTier("flex")
                .WithSpeed("fast"));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("priority", loaded.ResourceModels.Image.ServiceTier);
        Assert.Equal("fast", loaded.ResourceModels.Image.Speed);
        Assert.Equal("flex", loaded.CompactionModel.ServiceTier);
        Assert.Equal("fast", loaded.CompactionModel.Speed);

        var selected = loaded.ForCompaction(loaded.CompactionModel);
        Assert.Equal("flex", selected.ServiceTier);
        Assert.Equal("fast", selected.Speed);

        var image = loaded.ForResource(loaded.ResourceModels.Image);
        Assert.Equal("judge", image.Model);
        Assert.Equal("priority", image.ServiceTier);
        Assert.Equal("fast", image.Speed);

        store.Save(loaded
            .WithResourceModel(ResourceKind.Image, loaded.ResourceModels.Image.WithServiceTier(null).WithSpeed(null))
            .WithCompactionModel(loaded.CompactionModel.WithServiceTier(null).WithSpeed(null)));
        var json = File.ReadAllText(root.Home.ConfigPath);

        Assert.DoesNotContain("serviceTier", json, StringComparison.Ordinal);
        Assert.DoesNotContain("speed", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsASlotTheModelDoesNotList()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        var settings = store.Load();

        var image = Assert.Throws<InvalidOperationException>(() => settings.WithResourceModel(
            ResourceKind.Image,
            new ResourceModelSettings(true, "gateway", "plain").WithServiceTier("flex")));
        var compaction = Assert.Throws<InvalidOperationException>(() => settings.WithCompactionModel(
            new CompactionModelSettings(true, "gateway", "judge").WithSpeed("slow")));

        Assert.Contains("Image model service tier", image.Message, StringComparison.Ordinal);
        Assert.Contains("Compaction model speed", compaction.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SideModels_SendNoTierOrSpeedWhenUnsetEvenIfTheSessionSelectsOne()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        var settings = store.Load()
            .WithTier(new ProviderName("gateway"), "judge", "flex")
            .WithSpeed(new ProviderName("gateway"), "judge", "fast");

        var image = settings.ForResource(new ResourceModelSettings(true, "gateway", "judge"));
        var compaction = settings.ForCompaction(
            new CompactionModelSettings(true, "gateway", "judge").WithServiceTier("priority"));
        var reviewer = settings.ForReviewer(new ApprovalModelSettings(true, "gateway", "judge"));

        Assert.Null(image.ServiceTier);
        Assert.Null(image.Speed);
        Assert.Equal("priority", compaction.ServiceTier);
        Assert.Null(compaction.Speed);
        Assert.Null(reviewer.ServiceTier);
        Assert.Null(reviewer.Speed);
        Assert.Equal("flex", settings.TierFor(new ProviderName("gateway"), "judge"));
    }

    [Fact]
    public void Save_RoundTripsEachSideModelContextAndIgnoresTheSessionBudget()
    {
        using var root = new TemporaryHome();
        var store = SlotStore(root);
        var gateway = new ProviderName("gateway");
        var settings = store.Load()
            .WithContextWindow(gateway, "judge", 100_000)
            .WithApprovalModel(new ApprovalModelSettings(true, "gateway", "judge", contextWindow: 200_000))
            .WithExtendedApproval(new ExtendedApprovalSettings(new ApprovalModelSettings(true, "gateway", "judge")))
            .WithCompactionModel(new CompactionModelSettings(true, "gateway", "judge").WithContextWindow(200_000))
            .WithResourceModel(ResourceKind.Image, new ResourceModelSettings(true, "gateway", "judge").WithContextWindow(200_000));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(200_000, loaded.ApprovalModel.ContextWindow);
        Assert.Null(loaded.ExtendedApproval.Reviewer.ContextWindow);
        Assert.Equal(200_000, loaded.CompactionModel.ContextWindow);
        Assert.Equal(200_000, loaded.ResourceModels.Image.ContextWindow);
        Assert.Equal(200_000, loaded.ForReviewer(loaded.ApprovalModel).ContextWindow);
        Assert.Equal(200_000, loaded.ForCompaction(loaded.CompactionModel).ContextWindow);
        Assert.Equal(200_000, loaded.ForResource(loaded.ResourceModels.Image).ContextWindow);

        // Unset is the model's first budget, not the session's /context choice of 200K.
        var sessionWide = loaded.WithContextWindow(gateway, "judge", 200_000);
        Assert.Equal(100_000, sessionWide.ForReviewer(sessionWide.ExtendedApproval.Reviewer).ContextWindow);
        Assert.Contains("\"contextWindow\": 200000", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_RejectAContextTheSideModelDoesNotDeclare()
    {
        using var root = new TemporaryHome();
        var settings = SlotStore(root).Load();

        var exception = Assert.Throws<InvalidOperationException>(() => settings.WithCompactionModel(
            new CompactionModelSettings(true, "gateway", "judge").WithContextWindow(150_000)));

        Assert.Equal(
            "Compaction model context window is not available for this model. Available: 100K, 200K",
            exception.Message);
    }

    private static DiskSettingsStore SlotStore(TemporaryHome root)
    {
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ProvidersPath, SlotProviders);
        return store;
    }
}
