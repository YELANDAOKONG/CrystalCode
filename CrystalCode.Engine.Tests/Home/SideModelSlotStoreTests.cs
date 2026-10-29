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
            .WithImageModel(new ImageModelSettings("gateway", "judge")
                .WithServiceTier("priority")
                .WithSpeed("fast"))
            .WithCompactionModel(new CompactionModelSettings(true, "gateway", "judge")
                .WithServiceTier("flex")
                .WithSpeed("fast"));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("priority", loaded.ImageModel.ServiceTier);
        Assert.Equal("fast", loaded.ImageModel.Speed);
        Assert.Equal("flex", loaded.CompactionModel.ServiceTier);
        Assert.Equal("fast", loaded.CompactionModel.Speed);

        var selected = loaded.ForCompaction(loaded.CompactionModel);
        Assert.Equal("flex", selected.ServiceTier);
        Assert.Equal("fast", selected.Speed);

        store.Save(loaded
            .WithImageModel(loaded.ImageModel.WithServiceTier(null).WithSpeed(null))
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

        var image = Assert.Throws<InvalidOperationException>(() => settings.WithImageModel(
            new ImageModelSettings("gateway", "plain").WithServiceTier("flex")));
        var compaction = Assert.Throws<InvalidOperationException>(() => settings.WithCompactionModel(
            new CompactionModelSettings(true, "gateway", "judge").WithSpeed("slow")));

        Assert.Contains("Image model service tier", image.Message, StringComparison.Ordinal);
        Assert.Contains("Compaction model speed", compaction.Message, StringComparison.Ordinal);
    }

    private static DiskSettingsStore SlotStore(TemporaryHome root)
    {
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ProvidersPath, SlotProviders);
        return store;
    }
}
