using Crystal.Reasoning;

using CrystalCode.Configuration;
using CrystalCode.Home;

using Xunit;

namespace CrystalCode.Tests.Home;

public sealed class SettingsStoreTests
{
    [Fact]
    public void LoadOrCreate_WritesStarterCatalog()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);

        var settings = store.LoadOrCreate();

        Assert.Equal(ProviderName.DeepSeek, settings.Provider);
        Assert.Equal("deepseek-v4-flash", settings.Model);
        Assert.Equal(1_000_000, settings.ActiveModel.ContextWindow);
        Assert.True(settings.ActiveModel.Thinking);
        Assert.Equal(["low", "high", "maximum"], settings.ActiveModel.ThinkingEfforts);
        Assert.Equal(ThinkingSelection.Default, settings.ThinkingEffort);
        Assert.True(settings.Skills);
        Assert.True(File.Exists(root.Home.ConfigPath));
    }

    [Fact]
    public void Load_OverlaysCompatibleProviderAndModelParameters()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "openrouter",
              "model": "anthropic/claude-sonnet-4",
              "providers": {
                "openrouter": {
                  "protocol": "openai",
                  "baseUri": "https://openrouter.ai/api/v1/",
                  "tokenLimit": "max_tokens",
                  "models": {
                    "anthropic/claude-sonnet-4": {
                      "contextWindow": 200000,
                      "temperature": 0.2,
                      "maxTokens": 4096
                    }
                  }
                }
              }
            }
            """);

        var settings = store.Load();

        Assert.Equal("openrouter", settings.Provider.Value);
        Assert.Equal("anthropic/claude-sonnet-4", settings.Model);
        Assert.Equal(ProviderProtocol.OpenAI, settings.ActiveProvider.Protocol);
        Assert.Equal(TokenLimitStyle.MaxTokens, settings.ActiveProvider.TokenLimit);
        Assert.Equal(200000, settings.ActiveModel.ContextWindow);
        Assert.Equal(0.2, settings.ActiveModel.Temperature);
        Assert.Equal(4096, settings.ActiveModel.MaxTokens);
        Assert.False(settings.ActiveModel.Thinking);
        Assert.Equal(ThinkingSelection.Default, settings.ThinkingEffort);
        Assert.True(settings.Catalog.Providers.ContainsKey("deepseek"));
    }

    [Fact]
    public void Load_ReadsHostThinkingEffortAndModelCapability()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "thinkingEffort": "high",
              "providers": {
                "deepseek": {
                  "protocol": "deepseek",
                  "baseUri": "https://api.deepseek.com/",
                  "models": {
                    "deepseek-v4-flash": {
                      "contextWindow": 1000000,
                      "thinking": true,
                      "thinkingEfforts": ["low", "high", "maximum"]
                    }
                  }
                }
              }
            }
            """);

        var settings = store.Load();

        Assert.Equal(ThinkingSelection.Parse("high"), settings.ThinkingEffort);
        Assert.True(settings.ActiveModel.Thinking);
        Assert.Equal(ReasoningMode.Enabled, settings.ResolveReasoning()?.Mode);
        Assert.Equal(ReasoningEffort.High, settings.ResolveReasoning()?.Effort);
    }

    [Fact]
    public void Load_ReadsSkillsDisabled()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "skills": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.Skills);
        store.Save(settings);
        Assert.Contains("\"skills\": false", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_WritesProviderAndModelSelection()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithSelection(
            new ProviderName("openai"),
            "gpt-5.6-sol");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("openai", loaded.Provider.Value);
        Assert.Equal("gpt-5.6-sol", loaded.Model);
    }
}

