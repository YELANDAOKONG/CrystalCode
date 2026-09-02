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
        Assert.True(settings.ExternalTools);
        Assert.Equal(ExternalToolTrustPolicy.Author, settings.ExternalToolApproval.Home);
        Assert.Equal(ExternalToolTrustPolicy.Host, settings.ExternalToolApproval.Project);
        Assert.False(settings.EstimatedTokens);
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
    public void Load_ReadsExternalToolsDisabled()
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
              "externalTools": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.ExternalTools);
        store.Save(settings);
        Assert.Contains(
            "\"externalTools\": false",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RoundTripsExternalToolApprovalPolicies()
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
              "externalToolApproval": {
                "home": "host",
                "project": "author"
              }
            }
            """);

        var settings = store.Load();

        Assert.Equal(ExternalToolTrustPolicy.Host, settings.ExternalToolApproval.Home);
        Assert.Equal(ExternalToolTrustPolicy.Author, settings.ExternalToolApproval.Project);
        store.Save(settings);
        var json = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"home\": \"host\"", json, StringComparison.Ordinal);
        Assert.Contains("\"project\": \"author\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsEstimatedTokensEnabled()
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
              "estimatedTokens": true
            }
            """);

        var settings = store.Load();

        Assert.True(settings.EstimatedTokens);
        store.Save(settings);
        Assert.Contains(
            "\"estimatedTokens\": true",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsVerboseDisplayFlags()
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
              "verboseTools": false,
              "verboseCommands": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.VerboseTools);
        Assert.False(settings.VerboseCommands);
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

    [Fact]
    public void Save_RoundTripsNonDefaultPromptSet()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithPromptSet("concise");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("concise", loaded.PromptSet);
        Assert.Contains(
            "\"promptSet\": \"concise\"",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_OmitsDefaultPromptSet()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);

        store.Save(HarnessSettings.CreateDefault());

        Assert.DoesNotContain(
            "promptSet",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsExportDirectory()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithExportDirectory("workspace");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("workspace", loaded.ExportDirectory);
    }

    [Fact]
    public void Save_RoundTripsHomeExportDirectory()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithExportDirectory("home");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("home", loaded.ExportDirectory);
    }

    [Fact]
    public void Save_ClearsExportDirectoryWhenUnset()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.Save(store.LoadOrCreate().WithExportDirectory("workspace"));
        store.Save(store.Load().WithExportDirectory(null));

        var loaded = store.Load();

        Assert.Null(loaded.ExportDirectory);
        Assert.DoesNotContain(
            "exportDirectory",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsCustomStatusLine()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithStatusLine(
            new StatusLineSettings(true, ["context-left", "session-total"]));

        store.Save(settings);
        var loaded = store.Load();

        Assert.True(loaded.StatusLine.Enabled);
        Assert.Equal(["context-left", "session-total"], loaded.StatusLine.Fields);
    }
}
