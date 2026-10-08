using System.Text.Json;

using Crystal.Reasoning;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Load_AllowsKeylessCompatibleEndpointAndPreservesOllamaDefault()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ProvidersPath,
            """
            {
              "local": {
                "protocol": "openai",
                "baseUri": "http://localhost:9000/v1/",
                "requiresApiKey": false,
                "models": { "model": { "contextWindow": 8192 } }
              },
              "ollama": {
                "protocol": "ollama",
                "baseUri": "http://localhost:11434/",
                "models": { "custom": { "contextWindow": 8192 } }
              }
            }
            """);

        var settings = store.Load();

        Assert.False(settings.Catalog.Get(new ProviderName("local"))[0].RequiresApiKey);
        Assert.False(settings.Catalog.GetModelProvider(ProviderName.Ollama, "custom").RequiresApiKey);
    }

    [Fact]
    public void ProviderDocument_PreservesExplicitOllamaKeyRequirement()
    {
        var catalog = new ProviderCatalog([
            new ProviderDefinition(
                ProviderName.Ollama,
                ProviderProtocol.Ollama,
                new Uri("https://ollama.example/"),
                new Dictionary<string, ModelSettings> { ["model"] = new(8192) },
                requiresApiKey: true)
        ]);

        var document = SettingsMapper.WriteProviders(catalog);
        var provider = SettingsMapper.ReadProviders(document).Single();

        Assert.True(provider.RequiresApiKey);
        Assert.True(document.GetProperty("ollama").GetProperty("requiresApiKey").GetBoolean());
    }

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
        Assert.Equal(1024, settings.ExecutionBudget.MaximumModelCalls);
        Assert.Equal(8192, settings.ExecutionBudget.MaximumToolCalls);
        Assert.Equal(TimeSpan.FromDays(7), settings.ExecutionBudget.MaximumDuration);
        Assert.Equal(120, settings.BashTimeoutSeconds);
        Assert.True(File.Exists(root.Home.ConfigPath));
        Assert.False(File.Exists(root.Home.ProvidersPath));
        using var config = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.False(config.RootElement.TryGetProperty("providers", out _));
        Assert.False(config.RootElement.TryGetProperty("bashTimeoutSeconds", out _));
    }

    [Fact]
    public void Load_ExecutionBudgetSupportsPartialOverridesAndUnlimitedDimensions()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """
            {
              "executionBudget": {
                "maximumModelCalls": null,
                "maximumToolCalls": 0,
                "maximumDurationSeconds": 120.5
              }
            }
            """);

        var settings = store.Load();

        Assert.Null(settings.ExecutionBudget.MaximumModelCalls);
        Assert.Equal(0, settings.ExecutionBudget.MaximumToolCalls);
        Assert.Equal(TimeSpan.FromSeconds(120.5), settings.ExecutionBudget.MaximumDuration);
        store.Save(settings.WithVerboseTools(false));
        var reloaded = store.Load();
        Assert.Equal(settings.ExecutionBudget, reloaded.ExecutionBudget);
        using var saved = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.Equal(
            JsonValueKind.Null,
            saved.RootElement.GetProperty("executionBudget")
                .GetProperty("maximumModelCalls").ValueKind);
    }

    [Fact]
    public void Load_ExecutionBudgetUnlimitedRoundTrips()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """
            { "executionBudget": "unlimited" }
            """);

        var settings = store.Load();

        Assert.Equal(TurnLimits.Unlimited, settings.ExecutionBudget);
        store.Save(settings);
        Assert.Equal(TurnLimits.Unlimited, store.Load().ExecutionBudget);
    }

    [Theory]
    [InlineData("{ \"maximumModelCalls\": 0 }")]
    [InlineData("{ \"maximumToolCalls\": -1 }")]
    [InlineData("{ \"maximumDurationSeconds\": 0 }")]
    [InlineData("{ \"maximumDurationSeconds\": 999999999 }")]
    [InlineData("{ \"maximumModelCalls\": \"many\" }")]
    [InlineData("{ \"maximumModelCall\": 5 }")]
    public void Load_RejectsInvalidExecutionBudget(string budget)
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, "{ \"executionBudget\": " + budget + " }");

        var error = Record.Exception(() => store.Load());
        Assert.True(error is JsonException or ArgumentOutOfRangeException);
    }

    [Theory]
    [InlineData("\"unlimited\"")]
    [InlineData("null")]
    public void Load_BashTimeoutUnlimitedRoundTrips(string value)
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"bashTimeoutSeconds\": " + value + " }");

        var settings = store.Load();

        Assert.Null(settings.BashTimeoutSeconds);
        store.Save(settings.WithVerboseTools(false));
        Assert.Null(store.Load().BashTimeoutSeconds);
        using var saved = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.Equal(
            "unlimited",
            saved.RootElement.GetProperty("bashTimeoutSeconds").GetString());
    }

    [Fact]
    public void Load_BashTimeoutCustomSecondsRoundTrips()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, "{ \"bashTimeoutSeconds\": 30 }");

        var settings = store.Load();

        Assert.Equal(30, settings.BashTimeoutSeconds);
        store.Save(settings);
        Assert.Equal(30, store.Load().BashTimeoutSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("4294968")]
    [InlineData("\"forever\"")]
    [InlineData("true")]
    public void Load_RejectsInvalidBashTimeout(string value)
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"bashTimeoutSeconds\": " + value + " }");

        Assert.Throws<JsonException>(() => store.Load());
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
        Assert.Equal(HarnessSettings.DefaultPromptSet, store.Load().PromptSet);
        Assert.DoesNotContain(
            "promptSet",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
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
    public void Load_OmitsWorkspaceTrustWhenEnabled()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);

        var settings = store.LoadOrCreate();

        Assert.True(settings.WorkspaceTrust);
        Assert.DoesNotContain(
            "workspaceTrust",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsWorkspaceTrustDisabled()
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
              "workspaceTrust": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.WorkspaceTrust);
        store.Save(settings);
        Assert.Contains(
            "\"workspaceTrust\": false",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsPluginsDisabled()
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
              "plugins": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.Plugins);
        store.Save(settings);
        Assert.Contains(
            "\"plugins\": false",
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
              "verboseCommands": false,
              "verboseApprovals": false,
              "verboseThinking": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.VerboseTools);
        Assert.False(settings.VerboseCommands);
        Assert.False(settings.VerboseApprovals);
        Assert.False(settings.VerboseThinking);
        store.Save(settings.WithVerboseApprovals(true).WithVerboseThinking(true));
        var saved = File.ReadAllText(root.Home.ConfigPath);
        Assert.DoesNotContain("verboseApprovals", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("verboseThinking", saved, StringComparison.Ordinal);
        var loaded = store.Load();
        Assert.True(loaded.VerboseApprovals);
        Assert.True(loaded.VerboseThinking);
    }

    [Fact]
    public void Load_ReadsShowCompactionSummary()
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
              "showCompactionSummary": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.ShowCompactionSummary);
        store.Save(settings);
        Assert.Contains(
            "\"showCompactionSummary\": false",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        store.Save(settings.WithShowCompactionSummary(true));
        Assert.DoesNotContain(
            "showCompactionSummary",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        Assert.True(store.Load().ShowCompactionSummary);
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
    public void Save_OmitsPromptSetAndPromptAttachments()
    {
        using var root = new TemporaryHome();
        root.Home.EnsureCreated();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "promptSet": "concise",
              "promptAttachments": [ "beta", "alpha" ]
            }
            """);
        var store = new SettingsStore(root.Home);
        var loaded = store.Load();

        Assert.Equal(HarnessSettings.DefaultPromptSet, loaded.PromptSet);
        Assert.Empty(loaded.PromptAttachments);
        Assert.Null(loaded.PromptSetOverride);
        Assert.True(loaded.UsePromptAttachments);

        store.Save(loaded
            .WithPromptSet("concise")
            .WithPromptAttachments(["beta", "alpha"])
            .WithPromptSetOverride("strict")
            .WithUsePromptAttachments(false));
        var json = File.ReadAllText(root.Home.ConfigPath);
        var again = store.Load();

        Assert.DoesNotContain("promptSet", json, StringComparison.Ordinal);
        Assert.DoesNotContain("promptAttachments", json, StringComparison.Ordinal);
        Assert.Equal(HarnessSettings.DefaultPromptSet, again.PromptSet);
        Assert.Empty(again.PromptAttachments);
        Assert.Null(again.PromptSetOverride);
        Assert.True(again.UsePromptAttachments);
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

    [Fact]
    public void Save_RoundTripsAnEnabledApprovalModel()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithApprovalModel(
            new ApprovalModelSettings(true, "openai", "gpt-5.6-sol"));

        store.Save(settings);
        var loaded = store.Load();
        var json = File.ReadAllText(root.Home.ConfigPath);

        Assert.True(loaded.ApprovalModel.Enabled);
        Assert.Equal("openai", loaded.ApprovalModel.Provider);
        Assert.Equal("gpt-5.6-sol", loaded.ApprovalModel.Model);
        Assert.Contains("\"enabled\": true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsAndOmitsTheApprovalThinkingGear()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithApprovalModel(
            new ApprovalModelSettings(
                true,
                "openai",
                "gpt-5.6-sol",
                ThinkingSelection.Parse("high")));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("high", loaded.ApprovalModel.ThinkingEffort.Value);
        Assert.Contains(
            "\"thinkingEffort\": \"high\"",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);

        store.Save(loaded.WithApprovalModel(
            loaded.ApprovalModel.WithThinkingEffort(ThinkingSelection.Default)));
        var after = store.Load();

        Assert.Equal(ThinkingSelection.Default, after.ApprovalModel.ThinkingEffort);
        Assert.DoesNotContain(
            "thinkingEffort",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_KeepsADisabledApprovalModelWithoutEnablingIt()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithApprovalModel(
            new ApprovalModelSettings(false, "openai", "not-a-model"));

        store.Save(settings);
        var loaded = store.Load();

        Assert.False(loaded.ApprovalModel.Enabled);
        Assert.Equal("openai", loaded.ApprovalModel.Provider);
        Assert.Equal("not-a-model", loaded.ApprovalModel.Model);
        Assert.DoesNotContain(
            "\"enabled\"",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_OmitsAnUnsetApprovalModel()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);

        store.Save(HarnessSettings.CreateDefault());
        var loaded = store.Load();

        Assert.False(loaded.ApprovalModel.Enabled);
        Assert.False(loaded.ApprovalModel.HasSelection);
        Assert.DoesNotContain(
            "approvalModel",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsAnEnabledApprovalModelThatIsIncompleteOrUnknown()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "approvalModel": { "enabled": true } }
            """);
        var incomplete = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal(
            "approvalModel.enabled requires provider and model.",
            incomplete.Message);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "approvalModel": { "enabled": true, "provider": "openai", "model": "not-a-model" } }
            """);
        var unknown = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Contains("not-a-model", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsImageModelAndOmitsTheDefaultThinkingGear()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithImageModel(
            new ImageModelSettings(
                "openai",
                "gpt-5.6-sol",
                ThinkingSelection.Parse("high")));

        store.Save(settings);
        var loaded = store.Load();
        var json = File.ReadAllText(root.Home.ConfigPath);

        Assert.Equal("openai", loaded.ImageModel.Provider);
        Assert.Equal("gpt-5.6-sol", loaded.ImageModel.Model);
        Assert.Equal("high", loaded.ImageModel.ThinkingEffort.Value);
        Assert.Contains("\"thinkingEffort\": \"high\"", json, StringComparison.Ordinal);

        store.Save(loaded.WithImageModel(
            loaded.ImageModel.WithThinkingEffort(ThinkingSelection.Default)));
        var after = store.Load();

        Assert.Equal(ThinkingSelection.Default, after.ImageModel.ThinkingEffort);
        Assert.DoesNotContain(
            "thinkingEffort",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_OmitsAnUnsetImageModel()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);

        store.Save(HarnessSettings.CreateDefault());
        var loaded = store.Load();

        Assert.False(loaded.ImageModel.IsConfigured);
        Assert.DoesNotContain(
            "imageModel",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsAnImageModelThatIsIncompleteUnknownOrTextOnly()
    {
        using var root = new TemporaryHome();
        var store = new SettingsStore(root.Home);
        store.LoadOrCreate();

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "imageModel": { "provider": "openai" } }
            """);
        var incomplete = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal("imageModel requires provider and model.", incomplete.Message);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "imageModel": { "provider": "openai", "model": "not-a-model" } }
            """);
        var unknown = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Contains("not-a-model", unknown.Message, StringComparison.Ordinal);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "imageModel": { "provider": "deepseek", "model": "deepseek-v4-pro" } }
            """);
        var textOnly = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Contains("does not accept image input", textOnly.Message, StringComparison.Ordinal);
    }
}
