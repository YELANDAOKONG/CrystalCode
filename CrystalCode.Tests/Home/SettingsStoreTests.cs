using System.Text.Json;

using Crystal.Reasoning;

using CrystalCode.Configuration;
using CrystalCode.Home;

using Xunit;

namespace CrystalCode.Tests.Home;

public sealed class SettingsStoreTests
{
    [Fact]
    public void LoadOrCreate_UsesStarterCatalogWithoutWritingProviderDefinitions()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);

        var settings = store.LoadOrCreate();

        Assert.Equal(ProviderName.DeepSeek, settings.Provider);
        Assert.Equal("deepseek-flash", settings.Model);
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
        Assert.False(File.Exists(root.Home.ProvidersPath));
        using var config = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.False(config.RootElement.TryGetProperty("providers", out _));
    }

    [Fact]
    public void Load_PrefersProvidersFileOverLegacyDefinitions()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        root.Home.EnsureCreated();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "openrouter",
              "model": "current",
              "providers": {
                "openrouter": {
                  "protocol": "openai",
                  "baseUri": "https://legacy.example.test/",
                  "models": { "old": { "contextWindow": 1000 } }
                }
              }
            }
            """);
        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              "openrouter": {
                "protocol": "openai",
                "baseUri": "https://current.example.test/",
                "models": { "current": { "contextWindow": 2000 } }
              }
            }
            """);

        var settings = store.Load();

        Assert.Equal("current", settings.Model);
        Assert.Equal(2000, settings.ActiveModel.ContextWindow);
        Assert.Equal("https://current.example.test/", settings.ActiveProvider.BaseUri.AbsoluteUri);
        Assert.DoesNotContain("old", settings.Catalog.GetModelNames(new ProviderName("openrouter")));
    }

    [Fact]
    public void Save_CopiesLegacyProvidersAndPreservesOriginalDefinitions()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        root.Home.EnsureCreated();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "openrouter",
              "model": "custom",
              "providers": {
                "openrouter": {
                  "protocol": "openai",
                  "baseUri": "https://example.test/",
                  "models": { "custom": { "contextWindow": 123456 } }
                }
              }
            }
            """);
        using var original = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));

        store.Save(store.Load().WithPromptSet("concise"));

        Assert.True(File.Exists(root.Home.ProvidersPath));
        using var copied = JsonDocument.Parse(File.ReadAllText(root.Home.ProvidersPath));
        using var saved = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        var oldProviders = original.RootElement.GetProperty("providers");
        Assert.True(JsonElement.DeepEquals(oldProviders, copied.RootElement));
        Assert.True(JsonElement.DeepEquals(oldProviders, saved.RootElement.GetProperty("providers")));
        Assert.Equal("concise", store.Load().PromptSet);
        Assert.Equal(123456, store.Load().ActiveModel.ContextWindow);
    }

    [Fact]
    public void Save_DoesNotRewriteExistingProvidersFile()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate();
        const string providers = """
            {
              "deepseek": {
                "protocol": "deepseek",
                "baseUri": "https://api.deepseek.com/",
                "models": { "deepseek-flash": { "contextWindow": 123456 } }
              }
            }
            """;
        File.WriteAllText(root.Home.ProvidersPath, providers);

        store.Save(settings.WithPromptSet("concise"));

        Assert.Equal(providers, File.ReadAllText(root.Home.ProvidersPath));
        Assert.Equal(123456, store.Load().ActiveModel.ContextWindow);
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
    public void Load_GroupsProtocolsUnderOneProviderAndRoundTrips()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "openrouter",
              "model": "claude",
              "providers": {
                "openrouter": [
                  {
                    "protocol": "anthropic",
                    "baseUri": "https://example.test/anthropic/",
                    "apiKeyEnvironment": "ANTHROPIC_GATEWAY_KEY",
                    "models": { "claude": { "contextWindow": 200000 } }
                  },
                  {
                    "protocol": "openai",
                    "baseUri": "https://example.test/openai/",
                    "apiKeyEnvironment": "OPENAI_GATEWAY_KEY",
                    "models": { "gpt": { "contextWindow": 128000 } }
                  }
                ]
              }
            }
            """);

        var settings = store.Load();
        Assert.Equal(ProviderProtocol.Anthropic, settings.ActiveProvider.Protocol);
        Assert.Equal("ANTHROPIC_GATEWAY_KEY", settings.ActiveProvider.ApiKeyEnvironment);
        Assert.Equal(ProviderProtocol.OpenAI, settings.WithSelection(
            new ProviderName("openrouter"), "gpt").ActiveProvider.Protocol);
        Assert.Equal(["claude", "gpt"], settings.Catalog.GetModelNames(
            new ProviderName("openrouter")));
        Assert.True(ModelSelection.TryResolve(
            settings.Catalog,
            settings.Provider,
            "openrouter gpt",
            out var selected,
            out _));
        Assert.Equal("gpt", selected?.Model);

        store.Save(settings);
        var reloaded = store.Load();
        Assert.Equal(2, reloaded.Catalog.Providers["openrouter"].Count);
        Assert.Equal(ProviderProtocol.OpenAI, reloaded.WithSelection(
            new ProviderName("openrouter"), "gpt").ActiveProvider.Protocol);
    }

    [Fact]
    public void Load_RejectsDuplicateModelAcrossProtocols()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "providers": {
                "openrouter": [
                  {
                    "protocol": "anthropic",
                    "baseUri": "https://example.test/anthropic/",
                    "models": { "shared": { "contextWindow": 200000 } }
                  },
                  {
                    "protocol": "openai",
                    "baseUri": "https://example.test/openai/",
                    "models": { "shared": { "contextWindow": 128000 } }
                  }
                ]
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(store.Load);
        Assert.Contains("shared", error.Message, StringComparison.Ordinal);
        Assert.Contains("openrouter", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsRepeatedProviderKeyInsteadOfDroppingAnEndpoint()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "providers": {
                "openrouter": {
                  "protocol": "anthropic",
                  "baseUri": "https://example.test/anthropic/",
                  "models": { "claude": { "contextWindow": 200000 } }
                },
                "openrouter": {
                  "protocol": "openai",
                  "baseUri": "https://example.test/openai/",
                  "models": { "gpt": { "contextWindow": 128000 } }
                }
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(store.Load);
        Assert.Contains("Use an array", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsRepeatedModelKeyInOneEndpoint()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "providers": {
                "openrouter": {
                  "protocol": "openai",
                  "baseUri": "https://example.test/openai/",
                  "models": {
                    "shared": { "contextWindow": 128000 },
                    "shared": { "contextWindow": 200000 }
                  }
                }
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(store.Load);
        Assert.Contains("shared", error.Message, StringComparison.Ordinal);
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
