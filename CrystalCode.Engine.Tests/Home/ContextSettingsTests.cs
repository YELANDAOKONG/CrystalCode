using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class ContextSettingsTests
{
    [Fact]
    public void LoadAndSave_PreservesPerModelChoicesAndProviderDefinitions()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        const string providers = """
            {
              "local": {
                "protocol": "ollama",
                "baseUri": "http://localhost:11434/",
                "models": {
                  "large": { "contextWindow": 1000000, "contextWindows": [1000000, 272000] },
                  "small": { "contextWindow": 128000 }
                }
              }
            }
            """;
        File.WriteAllText(root.Home.ProvidersPath, providers);
        var settings = store.Load().WithSelection(new ProviderName("local"), "large");
        Assert.Equal(272000, settings.ContextWindow);

        store.Save(settings.WithContextWindow(settings.Provider, "large", 1000000));
        var reloaded = store.Load();
        Assert.Equal(1000000, reloaded.ContextWindow);
        var small = reloaded.WithSelection(settings.Provider, "small").WithApproval(ApprovalMode.Edit);
        Assert.Equal(128000, small.ContextWindow);
        Assert.Equal(1000000, small.WithSelection(settings.Provider, "large").ContextWindow);
        Assert.Equal(providers, File.ReadAllText(root.Home.ProvidersPath));

        var written = SettingsMapper.WriteProviders(reloaded.Catalog);
        var roundTrip = SettingsMapper.ReadProviders(written).Single(provider => provider.Name.Value == "local");
        Assert.Equal([272000, 1000000], roundTrip.Models["large"].ContextWindows);
    }

    [Fact]
    public void Load_RemovedSavedWindowFallsBackToLowestAvailable()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """
            {
              "contextSelections": { "deepseek/deepseek-flash": 123 }
            }
            """);

        var settings = store.Load();
        Assert.Equal(settings.ActiveModel.ContextWindows[0], settings.ContextWindow);
    }
}
