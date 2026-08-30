using CrystalHarness.Configuration;
using CrystalHarness.Home;

using Xunit;

namespace CrystalHarness.Tests.Home;

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
        Assert.True(settings.Catalog.Providers.ContainsKey("deepseek"));
    }
}
