using System.Text.Json;

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

    [Fact]
    public void Save_WritesNestedSelectionsAndKeepsModelIdsWithSlash()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ProvidersPath, """
            {
              "openrouter": {
                "protocol": "openai",
                "baseUri": "https://openrouter.ai/api/v1/",
                "models": {
                  "deepseek/deepseek-v4-flash": {
                    "contextWindow": 1000000,
                    "contextWindows": [272000, 1000000]
                  }
                }
              }
            }
            """);
        var provider = new ProviderName("openrouter");
        const string model = "deepseek/deepseek-v4-flash";
        var settings = store.Load().WithSelection(provider, model);
        Assert.Equal(272000, settings.ContextWindow);

        store.Save(settings.WithContextWindow(provider, model, 1000000));

        using var written = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.Equal(
            1000000,
            written.RootElement
                .GetProperty("contextSelections")
                .GetProperty("openrouter")
                .GetProperty(model)
                .GetInt32());
        Assert.Equal(1000000, store.Load().ContextWindow);
    }

    [Fact]
    public void Load_ReadsFlatSelectionsAndSavesThemNested()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """
            {
              "contextSelections": {
                "DeepSeek/deepseek-flash": 1000000,
                "retired/other-model": 272000
              }
            }
            """);

        var settings = store.Load();
        Assert.Equal(1000000, settings.ContextWindow);
        Assert.Equal(
            1000000,
            settings.ContextSelections[
                new ContextSelectionKey(new ProviderName("deepseek"), "deepseek-flash")]);
        Assert.Equal(
            272000,
            settings.ContextSelections[
                new ContextSelectionKey(new ProviderName("retired"), "other-model")]);

        store.Save(settings);

        using var written = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        var selections = written.RootElement.GetProperty("contextSelections");
        Assert.Equal(["deepseek", "retired"], [.. selections.EnumerateObject().Select(entry => entry.Name)]);
        Assert.Equal(1000000, selections.GetProperty("deepseek").GetProperty("deepseek-flash").GetInt32());
        Assert.Equal(272000, selections.GetProperty("retired").GetProperty("other-model").GetInt32());
    }

    [Theory]
    [InlineData("""{ "contextSelections": [1000000] }""")]
    [InlineData("""{ "contextSelections": { "deepseek-flash": 1000000 } }""")]
    [InlineData("""{ "contextSelections": { "deepseek": { "deepseek-flash": "1m" } } }""")]
    [InlineData("""{ "contextSelections": { "deepseek": { "deepseek-flash": 0 } } }""")]
    [InlineData("""{ "contextSelections": { "deepseek": { " deepseek-flash": 1000000 } } }""")]
    [InlineData("""{ "contextSelections": { "deepseek": { "deepseek-flash": 1000000, "deepseek-flash": 272000 } } }""")]
    [InlineData("""{ "contextSelections": { "deepseek/deepseek-flash": 1000000, "deepseek": { "deepseek-flash": 272000 } } }""")]
    public void Load_RefusesMalformedSelections(string json)
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, json);

        Assert.Throws<JsonException>(() => store.Load());
    }
}
