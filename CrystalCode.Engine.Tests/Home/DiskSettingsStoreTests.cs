using System.Text.Json;

using Crystal.Reasoning;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class DiskSettingsStoreTests
{
    [Fact]
    public void Load_AllowsKeylessCompatibleEndpointAndPreservesOllamaDefault()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
    public void Load_ReadsJsoncProvidersFile()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        const string jsonc = """
            {
              // A mixed-protocol gateway.
              "openrouter": [
                {
                  "protocol": "anthropic",
                  "baseUri": "https://example.test/anthropic/",
                  "options": {
                    // Hide the reasoning display.
                    "thinkingDisplay": "omitted",
                  },
                  "models": {
                    // Keep the old model disabled for now.
                    // "legacy": { "contextWindow": 1000 },
                    "claude": { "contextWindow": 200000, },
                  },
                },
                {
                  "protocol": "openai",
                  "baseUri": "https://example.test/openai/",
                  /* The second protocol. */
                  "models": { "gpt": { "contextWindow": 128000, }, },
                },
              ],
            }
            """;
        File.WriteAllText(root.Home.ProvidersPath, jsonc);

        var settings = store.Load();

        Assert.Equal(
            200000,
            settings.Catalog.GetModel(new ProviderName("openrouter"), "claude").ContextWindow);
        Assert.Equal(
            ThinkingDisplay.Omitted,
            settings.Catalog.Get(new ProviderName("openrouter"))[0].ThinkingDisplay);
        Assert.Equal(
            128000,
            settings.Catalog.GetModel(new ProviderName("openrouter"), "gpt").ContextWindow);
        Assert.DoesNotContain(
            "legacy",
            settings.Catalog.GetModelNames(new ProviderName("openrouter")));
        var before = File.ReadAllText(root.Home.ProvidersPath);

        store.Save(settings);

        Assert.Equal(before, File.ReadAllText(root.Home.ProvidersPath));
    }

    [Fact]
    public void Load_ReadsProviderResourceTrimOverGlobalAndRejectsInvalidValues()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              "gateway": {
                "protocol": "openai",
                "baseUri": "https://example.test/openai/",
                "resourceTrim": { "keepRecent": 4, "maxBytes": 1000 },
                "models": { "gpt": { "contextWindow": 128000, "imageInput": true } }
              }
            }
            """);
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "gateway",
              "model": "gpt",
              "resourceTrim": { "slack": 2, "keepRecent": 9 }
            }
            """);

        var effective = store.Load().EffectiveResourceTrim;

        Assert.Equal(4, effective.KeepRecent);
        Assert.Equal(2, effective.Slack);
        Assert.Equal(1000, effective.MaxBytes);
        Assert.True(effective.Enabled);

        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              "gateway": {
                "protocol": "openai",
                "baseUri": "https://example.test/openai/",
                "resourceTrim": { "keepRecent": 0 },
                "models": { "gpt": { "contextWindow": 128000 } }
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains("providers.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("keep recent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_RoundTripsAndSurvivesAnotherSave()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var created = store.LoadOrCreate();
        store.Save(created);
        Assert.DoesNotContain("\"server\"", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);

        File.WriteAllText(
            root.Home.ConfigPath,
            File.ReadAllText(root.Home.ConfigPath).Replace(
                "{",
                """
                {
                  "server": {
                    "allowOrigins": ["https://app.example.test"],
                    "allowHosts": ["crystal.example.test"],
                    "exposeHeaders": ["X-Request-Id"],
                    "allowPrivateNetwork": true
                  },
                """,
                StringComparison.Ordinal));
        var loaded = store.Load();
        store.Save(loaded.WithApproval(ApprovalMode.Edit));
        var reloaded = store.Load();

        Assert.Equal(["https://app.example.test"], reloaded.Server.AllowOrigins);
        Assert.Equal(["crystal.example.test"], reloaded.Server.AllowHosts);
        Assert.Equal(["X-Request-Id"], reloaded.Server.ExposeHeaders);
        Assert.True(reloaded.Server.AllowPrivateNetwork);
    }

    [Theory]
    [InlineData("""{ "allowOrigins": ["https://app.example.test/path"] }""", "allowOrigins")]
    [InlineData("""{ "allowOrigins": ["app.example.test"] }""", "allowOrigins")]
    [InlineData("""{ "allowHosts": [" "] }""", "allowHosts")]
    [InlineData("""{ "exposeHeaders": ["Bad Header"] }""", "exposeHeaders")]
    public void Server_RejectsInvalidEntries(string section, string name)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            File.ReadAllText(root.Home.ConfigPath).Replace(
                "{",
                "{ \"server\": " + section + ",",
                StringComparison.Ordinal));

        var exception = Assert.Throws<InvalidOperationException>(store.Load);

        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsReservedOutputTokens()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate();

        store.Save(settings.WithReservedOutputTokens(4096));

        Assert.Equal(4096, store.Load().ReservedOutputTokens);
        Assert.Contains(
            "reservedOutputTokens",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);

        store.Save(settings.WithOutputTokenCap(1500));
        Assert.Null(store.Load().OutputTokenCap);
        Assert.DoesNotContain(
            "outputTokenCap",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsRepeatedProviderKeyInJsoncProvidersFile()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              // Duplicate provider names must still fail.
              "gateway": {
                "protocol": "openai",
                "baseUri": "https://one.example.test/",
                "models": { "one": { "contextWindow": 1024 } }
              },
              "gateway": {
                "protocol": "openai",
                "baseUri": "https://two.example.test/",
                "models": { "two": { "contextWindow": 1024 } }
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());

        Assert.Contains("Use an array", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsCommentsInConfigFile()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              // config.json stays strict JSON.
              "provider": "deepseek"
            }
            """);

        Assert.ThrowsAny<JsonException>(() => store.Load());
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
    public void ProviderDocument_WritesOmittedThinkingDisplayAndDropsTheDefault()
    {
        var omitted = new ProviderCatalog([
            Endpoint("anthropic", ProviderProtocol.Anthropic, ThinkingDisplay.Omitted)
        ]);
        var summarized = new ProviderCatalog([
            Endpoint("anthropic", ProviderProtocol.Anthropic, ThinkingDisplay.Summarized)
        ]);

        var omittedDocument = SettingsMapper.WriteProviders(omitted);
        var summarizedDocument = SettingsMapper.WriteProviders(summarized);

        Assert.Equal(
            "omitted",
            omittedDocument.GetProperty("anthropic").GetProperty("options").GetProperty("thinkingDisplay").GetString());
        Assert.Equal(ThinkingDisplay.Omitted, SettingsMapper.ReadProviders(omittedDocument).Single().ThinkingDisplay);
        Assert.False(summarizedDocument.GetProperty("anthropic").TryGetProperty("options", out _));
        Assert.Null(SettingsMapper.ReadProviders(summarizedDocument).Single().ThinkingDisplay);
    }

    [Fact]
    public void ProviderDocument_TreatsExplicitSummarizedThinkingDisplayAsTheDefault()
    {
        using var json = JsonDocument.Parse(
            """
            {
              "anthropic": {
                "protocol": "anthropic",
                "baseUri": "https://api.anthropic.com/v1/",
                "options": { "thinkingDisplay": "Summarized" },
                "models": { "claude": { "contextWindow": 200000 } }
              }
            }
            """);

        var provider = SettingsMapper.ReadProviders(json.RootElement).Single();
        var written = SettingsMapper.WriteProviders(new ProviderCatalog([provider]));

        Assert.Null(provider.ThinkingDisplay);
        Assert.False(written.GetProperty("anthropic").TryGetProperty("options", out _));
    }

    [Theory]
    [InlineData(
        "\"options\": {\"thinkingDisplay\": \"omitted\"}",
        "openai",
        "not valid for protocol")]
    [InlineData(
        "\"options\": {\"unknown\": true}",
        "anthropic",
        "not valid for protocol")]
    [InlineData(
        "\"options\": {\"thinkingDisplay\": {\"mode\": \"omitted\"}}",
        "anthropic",
        "JSON scalar")]
    [InlineData(
        "\"options\": {\"temperature\": 0.2}",
        "anthropic",
        "Set it on the model")]
    [InlineData(
        "\"options\": [\"omitted\"]",
        "anthropic",
        "must be an object")]
    [InlineData(
        "\"options\": {\"thinkingDisplay\": \"full\"}",
        "anthropic",
        "summarized or omitted")]
    public void ProviderDocument_RejectsInvalidEndpointOptions(
        string fields,
        string protocol,
        string message)
    {
        using var json = JsonDocument.Parse(
            $$"""
            {
              "gateway": {
                "protocol": "{{protocol}}",
                "baseUri": "https://example.test/",
                {{fields}},
                "models": { "model": { "contextWindow": 1024 } }
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(
            () => SettingsMapper.ReadProviders(json.RootElement));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Contains("gateway", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderDefinition_RejectsThinkingDisplayOnAnotherProtocol()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Endpoint("openai", ProviderProtocol.OpenAI, ThinkingDisplay.Omitted));

        Assert.Contains("anthropic protocol", error.Message, StringComparison.Ordinal);
    }

    private static ProviderDefinition Endpoint(
        string name,
        ProviderProtocol protocol,
        ThinkingDisplay? thinkingDisplay) =>
        new(
            new ProviderName(name),
            protocol,
            new Uri("https://example.test/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(1024) },
            thinkingDisplay: thinkingDisplay);

    [Fact]
    public void ProviderDocument_PreservesDeclaredTextToolsAndOmitsTheDefault()
    {
        var catalog = new ProviderCatalog([
            new ProviderDefinition(
                new ProviderName("gateway"),
                ProviderProtocol.Responses,
                new Uri("https://example.test/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["patcher"] = new(8192, textTools: true),
                    ["plain"] = new(8192)
                })
        ]);

        var document = SettingsMapper.WriteProviders(catalog);
        var models = document.GetProperty("gateway").GetProperty("models");

        Assert.True(models.GetProperty("patcher").GetProperty("textTools").GetBoolean());
        Assert.False(models.GetProperty("plain").TryGetProperty("textTools", out _));
        var provider = SettingsMapper.ReadProviders(document).Single();
        Assert.True(provider.Models["patcher"].TextTools);
        Assert.False(provider.Models["plain"].TextTools);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("gemini")]
    [InlineData("ollama")]
    [InlineData("deepseek")]
    public void ReadProviders_RejectsTextToolsOutsideTheResponsesProtocol(string protocol)
    {
        using var declared = JsonDocument.Parse(
            $$"""
            {
              "local": {
                "protocol": "{{protocol}}",
                "baseUri": "https://example.test/v1/",
                "models": { "m": { "contextWindow": 8192, "textTools": true } }
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(
            () => SettingsMapper.ReadProviders(declared.RootElement));

        Assert.Contains("textTools", error.Message, StringComparison.Ordinal);
        Assert.Contains("only the responses protocol", error.Message, StringComparison.Ordinal);
        Assert.Contains("'m'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadProviders_AcceptsTextToolsOnTheResponsesProtocol()
    {
        using var declared = JsonDocument.Parse(
            """
            {
              "gw": {
                "protocol": "Responses",
                "baseUri": "https://example.test/v1/",
                "models": { "m": { "contextWindow": 8192, "textTools": true } }
              }
            }
            """);

        var provider = SettingsMapper.ReadProviders(declared.RootElement).Single();

        Assert.True(provider.Models["m"].TextTools);
    }

    [Fact]
    public void ProviderDocument_PreservesDeclaredAudioAndVideoInput()
    {
        var catalog = new ProviderCatalog([
            new ProviderDefinition(
                new ProviderName("local"),
                ProviderProtocol.OpenAI,
                new Uri("https://example.test/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["heard"] = new(8192, audioInput: true),
                    ["seen"] = new(8192, imageInput: true),
                    ["watched"] = new(8192, videoInput: true)
                })
        ]);

        var document = SettingsMapper.WriteProviders(catalog);
        var models = document.GetProperty("local").GetProperty("models");
        var heard = models.GetProperty("heard");
        var seen = models.GetProperty("seen");
        var watched = models.GetProperty("watched");
        Assert.True(heard.GetProperty("audioInput").GetBoolean());
        Assert.False(heard.TryGetProperty("videoInput", out _));
        Assert.False(heard.TryGetProperty("imageInput", out _));
        Assert.True(seen.GetProperty("imageInput").GetBoolean());
        Assert.False(seen.TryGetProperty("audioInput", out _));
        Assert.False(seen.TryGetProperty("videoInput", out _));
        Assert.True(watched.GetProperty("videoInput").GetBoolean());
        Assert.False(watched.TryGetProperty("audioInput", out _));
        Assert.False(watched.TryGetProperty("imageInput", out _));

        var provider = SettingsMapper.ReadProviders(document).Single();

        Assert.True(provider.Models["heard"].AudioInput);
        Assert.False(provider.Models["heard"].VideoInput);
        Assert.False(provider.Models["heard"].ImageInput);
        Assert.False(provider.Models["seen"].AudioInput);
        Assert.False(provider.Models["seen"].VideoInput);
        Assert.True(provider.Models["seen"].ImageInput);
        Assert.False(provider.Models["watched"].AudioInput);
        Assert.True(provider.Models["watched"].VideoInput);
        Assert.False(provider.Models["watched"].ImageInput);

        using var declared = JsonDocument.Parse(
            """
            {
              "local": {
                "protocol": "openai",
                "baseUri": "https://example.test/v1/",
                "models": {
                  "both": {
                    "contextWindow": 8192,
                    "audioInput": true,
                    "videoInput": true
                  }
                }
              }
            }
            """);
        var both = SettingsMapper.ReadProviders(declared.RootElement).Single().Models["both"];
        Assert.True(both.AudioInput);
        Assert.True(both.VideoInput);
        Assert.False(both.ImageInput);
    }

    [Fact]
    public void LoadOrCreate_UsesStarterCatalogWithoutWritingProviderDefinitions()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);

        var settings = store.LoadOrCreate();

        Assert.Equal(ProviderName.DeepSeek, settings.Provider);
        Assert.Equal("deepseek-flash", settings.Model);
        Assert.Equal(1_000_000, settings.ActiveModel.ContextWindow);
        Assert.True(settings.ActiveModel.Thinking);
        Assert.Equal(["low", "high", "max"], settings.ActiveModel.ThinkingEfforts);
        Assert.Equal(ThinkingSelection.Default, settings.ThinkingEffort);
        Assert.True(settings.Skills);
        Assert.True(settings.ExternalTools);
        Assert.Equal(ExternalToolTrustPolicy.Author, settings.ExternalToolApproval.Home);
        Assert.Equal(ExternalToolTrustPolicy.Host, settings.ExternalToolApproval.Project);
        Assert.False(settings.EstimatedTokens);
        Assert.Equal(1024, settings.ExecutionBudget.MaximumModelCalls);
        Assert.Equal(8192, settings.ExecutionBudget.MaximumToolCalls);
        Assert.Equal(TimeSpan.FromDays(7), settings.ExecutionBudget.MaximumDuration);
        Assert.Equal(600, settings.BashTimeoutSeconds);
        Assert.Equal(10800, settings.BackgroundBashTimeoutSeconds);
        Assert.Equal(RetrySettings.Default, settings.Retry);
        Assert.Equal(RetrySettings.Default, settings.ApprovalRetry);
        Assert.True(File.Exists(root.Home.ConfigPath));
        Assert.False(File.Exists(root.Home.ProvidersPath));
        using var config = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.False(config.RootElement.TryGetProperty("providers", out _));
        Assert.False(config.RootElement.TryGetProperty("bashTimeoutSeconds", out _));
        Assert.False(config.RootElement.TryGetProperty("backgroundBashTimeoutSeconds", out _));
        Assert.False(config.RootElement.TryGetProperty("retry", out _));
        Assert.False(config.RootElement.TryGetProperty("approvalRetry", out _));
    }

    [Fact]
    public void Load_ReadsConfiguredRetryPolicies()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """
            {
              "retry": {
                "enabled": false,
                "maximumRetries": 2,
                "initialDelaySeconds": 1,
                "backoffFactor": 3,
                "jitterFactor": 0,
                "maximumDelaySeconds": 5
              },
              "approvalRetry": { "maximumRetries": 1 }
            }
            """);

        var settings = store.Load();

        Assert.False(settings.Retry.Enabled);
        Assert.Equal(2, settings.Retry.MaximumRetries);
        Assert.Equal(1d, settings.Retry.InitialDelaySeconds);
        Assert.Equal(3d, settings.Retry.BackoffFactor);
        Assert.Equal(0d, settings.Retry.JitterFactor);
        Assert.Equal(5d, settings.Retry.MaximumDelaySeconds);
        Assert.True(settings.ApprovalRetry.Enabled);
        Assert.Equal(1, settings.ApprovalRetry.MaximumRetries);
        Assert.Equal(
            SessionRetryOptions.DefaultInitialDelaySeconds,
            settings.ApprovalRetry.InitialDelaySeconds);
    }

    [Fact]
    public void Save_RoundTripsCustomRetryPolicyAndOmitsTheDefault()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithRetry(new RetrySettings(maximumRetries: 1));
        store.Save(settings.WithVerboseTools(false));

        var reloaded = store.Load();

        Assert.Equal(new RetrySettings(maximumRetries: 1), reloaded.Retry);
        Assert.Equal(RetrySettings.Default, reloaded.ApprovalRetry);
        using var saved = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.Equal(
            1,
            saved.RootElement.GetProperty("retry").GetProperty("maximumRetries").GetInt32());
        Assert.False(saved.RootElement.TryGetProperty("approvalRetry", out _));
    }

    [Fact]
    public void Load_RejectsInvalidRetryPolicy()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """{ "retry": { "maximumRetries": -1 } }""");

        Assert.Throws<ArgumentOutOfRangeException>(() => store.Load());
    }

    [Fact]
    public void Load_ExecutionBudgetSupportsPartialOverridesAndUnlimitedDimensions()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"bashTimeoutSeconds\": " + value + " }");

        Assert.Throws<JsonException>(() => store.Load());
    }

    [Theory]
    [InlineData("\"unlimited\"")]
    [InlineData("null")]
    public void Load_BackgroundBashTimeoutUnlimitedRoundTrips(string value)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"backgroundBashTimeoutSeconds\": " + value + " }");

        var settings = store.Load();

        Assert.Null(settings.BackgroundBashTimeoutSeconds);
        store.Save(settings.WithVerboseTools(false));
        Assert.Null(store.Load().BackgroundBashTimeoutSeconds);
        using var saved = JsonDocument.Parse(File.ReadAllText(root.Home.ConfigPath));
        Assert.Equal(
            "unlimited",
            saved.RootElement.GetProperty("backgroundBashTimeoutSeconds").GetString());
    }

    [Fact]
    public void Load_BackgroundBashTimeoutCustomSecondsRoundTrips()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, "{ \"backgroundBashTimeoutSeconds\": 1800 }");

        var settings = store.Load();

        Assert.Equal(1800, settings.BackgroundBashTimeoutSeconds);
        Assert.Equal(600, settings.BashTimeoutSeconds);
        store.Save(settings);
        Assert.Equal(1800, store.Load().BackgroundBashTimeoutSeconds);
    }

    [Fact]
    public void Load_KeepsBashAndBackgroundBashTimeoutsIndependent()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"bashTimeoutSeconds\": 30, \"backgroundBashTimeoutSeconds\": \"unlimited\" }");

        var settings = store.Load();

        Assert.Equal(30, settings.BashTimeoutSeconds);
        Assert.Null(settings.BackgroundBashTimeoutSeconds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("4294968")]
    [InlineData("\"forever\"")]
    [InlineData("true")]
    public void Load_RejectsInvalidBackgroundBashTimeout(string value)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"backgroundBashTimeoutSeconds\": " + value + " }");

        Assert.Throws<JsonException>(() => store.Load());
    }

    [Fact]
    public void Load_PrefersProvidersFileOverLegacyDefinitions()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);

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
        var store = new DiskSettingsStore(root.Home);
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
    public void Load_OmitsRuleFilesWhenBothAreOn()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);

        var settings = store.LoadOrCreate();

        Assert.True(settings.RuleFiles.Global);
        Assert.True(settings.RuleFiles.Project);
        Assert.DoesNotContain(
            "ruleFiles",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsProjectRuleFilesDisabledAndOmitsTheOnSide()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "ruleFiles": { "project": false }
            }
            """);

        var settings = store.Load();

        Assert.True(settings.RuleFiles.Global);
        Assert.False(settings.RuleFiles.Project);
        store.Save(settings);
        var saved = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"project\": false", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"global\"", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsPluginsDisabled()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
    public void Load_EnablesMcpByDefault_AndOmitsDefaultsOnSave()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);

        var settings = store.LoadOrCreate();

        Assert.True(settings.Mcp);
        Assert.Equal(McpToolApprovalSettings.Default, settings.McpToolApproval);
        store.Save(settings);
        var json = File.ReadAllText(root.Home.ConfigPath);
        Assert.DoesNotContain("\"mcp\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("mcpToolApproval", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsMcpDisabled()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "mcp": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.Mcp);
        store.Save(settings);
        Assert.Contains(
            "\"mcp\": false",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RoundTripsMcpToolApprovalPolicies()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "mcpToolApproval": {
                "home": "author"
              }
            }
            """);

        var settings = store.Load();

        Assert.Equal(ExternalToolTrustPolicy.Author, settings.McpToolApproval.Home);
        Assert.Equal(ExternalToolTrustPolicy.Host, settings.McpToolApproval.Project);
        store.Save(settings);
        var json = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"home\": \"author\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"project\"", json[json.IndexOf("mcpToolApproval", StringComparison.Ordinal)..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ThemeIsANameOrAnObjectAndApprovalColorsDefaultToOn()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();

        var plain = LoadWithTheme(root, store, "\"night\"");
        var grouped = LoadWithTheme(root, store, "{ \"name\": \"night\", \"approvalColors\": false }");
        var onlyOff = LoadWithTheme(root, store, "{ \"approvalColors\": false }");

        Assert.Equal(("night", true), (plain.Theme, plain.ApprovalColors));
        Assert.Equal(("night", false), (grouped.Theme, grouped.ApprovalColors));
        Assert.Equal(("default", false), (onlyOff.Theme, onlyOff.ApprovalColors));
        Assert.Equal(("default", true), (LoadWithTheme(root, store, null).Theme, true));
    }

    [Fact]
    public void Save_WritesThemeAsNameUntilApprovalColorsAreOff()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithTheme("night");

        store.Save(settings);
        Assert.Contains("\"theme\": \"night\"", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);

        store.Save(settings.WithApprovalColors(false));
        var saved = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"approvalColors\": false", saved, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"night\"", saved, StringComparison.Ordinal);
        Assert.False(store.Load().ApprovalColors);
        Assert.Equal("night", store.Load().Theme);

        store.Save(store.Load().WithTheme("default"));
        Assert.DoesNotContain("\"name\"", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);

        store.Save(store.Load().WithApprovalColors(true));
        Assert.DoesNotContain("theme", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsAThemeThatIsNotANameOrAnObject()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();

        var error = Assert.Throws<InvalidOperationException>(() => LoadWithTheme(root, store, "true"));

        Assert.Contains("theme", error.Message, StringComparison.Ordinal);
    }

    private static HarnessSettings LoadWithTheme(TemporaryHome root, DiskSettingsStore store, string? theme)
    {
        File.WriteAllText(
            root.Home.ConfigPath,
            "{ \"provider\": \"deepseek\", \"model\": \"deepseek-v4-flash\""
            + (theme is null ? string.Empty : ", \"theme\": " + theme)
            + " }");
        return store.Load();
    }

    [Fact]
    public void Load_ReadsEstimatedTokensEnabled()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "verboseTools": false,
              "verboseCommands": false,
              "verboseResults": false,
              "verboseCalls": false,
              "verboseApprovals": false,
              "verboseReview": false,
              "verboseThinking": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.VerboseTools);
        Assert.False(settings.VerboseCommands);
        Assert.False(settings.VerboseResults);
        Assert.False(settings.VerboseCalls);
        Assert.False(settings.VerboseApprovals);
        Assert.False(settings.VerboseReview);
        Assert.False(settings.VerboseThinking);
        store.Save(settings.WithVerboseResults(true).WithVerboseCalls(true).WithVerboseApprovals(true).WithVerboseReview(true).WithVerboseThinking(true));
        var saved = File.ReadAllText(root.Home.ConfigPath);
        Assert.DoesNotContain("verboseResults", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("verboseCalls", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("verboseApprovals", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("verboseReview", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("verboseThinking", saved, StringComparison.Ordinal);
        var loaded = store.Load();
        Assert.True(loaded.VerboseResults);
        Assert.True(loaded.VerboseCalls);
        Assert.True(loaded.VerboseApprovals);
        Assert.True(loaded.VerboseReview);
        Assert.True(loaded.VerboseThinking);
    }

    [Fact]
    public void Load_VerboseReviewStaysOnWhenApprovalsAreOff()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "verboseApprovals": false
            }
            """);

        var settings = store.Load();

        Assert.False(settings.VerboseApprovals);
        Assert.True(settings.VerboseReview);
    }

    [Fact]
    public void Load_VerboseResultsStaysOnWhenTheFieldIsAbsent()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        Assert.True(settings.VerboseResults);
    }

    [Fact]
    public void Load_ReadsShowCompactionSummary()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
    public void Load_ReadsShowAutomaticCompactionSummary()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "showAutomaticCompactionSummary": true
            }
            """);

        var settings = store.Load();

        Assert.True(settings.ShowAutomaticCompactionSummary);
        store.Save(settings);
        Assert.Contains(
            "\"showAutomaticCompactionSummary\": true",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        store.Save(settings.WithShowAutomaticCompactionSummary(false));
        Assert.DoesNotContain(
            "showAutomaticCompactionSummary",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        Assert.False(store.Load().ShowAutomaticCompactionSummary);
    }

    [Fact]
    public void Load_ReadsMicroCompactionAndOmitsTheDefault()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Assert.DoesNotContain(
            "microCompaction",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "microCompaction": {
                "enabled": true,
                "protectTurns": 3,
                "protectTokens": 1000,
                "minimumTokens": 0
              }
            }
            """);

        var settings = store.Load();

        Assert.True(settings.MicroCompaction.Enabled);
        Assert.Equal(3, settings.MicroCompaction.ProtectTurns);
        Assert.Equal(1000, settings.MicroCompaction.ProtectTokens);
        Assert.Equal(0, settings.MicroCompaction.MinimumTokens);
        store.Save(settings);
        var saved = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"enabled\": true", saved, StringComparison.Ordinal);
        Assert.Contains("\"protectTurns\": 3", saved, StringComparison.Ordinal);
        store.Save(settings.WithMicroCompaction(MicroCompactionSettings.Default));
        Assert.DoesNotContain(
            "microCompaction",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReadsResourceTrimAndOmitsTheDefault()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        Assert.DoesNotContain(
            "resourceTrim",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        Assert.True(store.Load().ResourceTrim.Enabled);
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "resourceTrim": {
                "enabled": false,
                "keepRecent": 3,
                "slack": 0,
                "maxBytes": 0,
                "includeUserResources": true
              }
            }
            """);

        var settings = store.Load();

        Assert.False(settings.ResourceTrim.Enabled);
        Assert.Equal(3, settings.ResourceTrim.KeepRecent);
        Assert.Equal(0, settings.ResourceTrim.Slack);
        Assert.Equal(0, settings.ResourceTrim.MaxBytes);
        Assert.True(settings.ResourceTrim.IncludeUserResources);
        store.Save(settings);
        var saved = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"keepRecent\": 3", saved, StringComparison.Ordinal);
        store.Save(settings.WithResourceTrim(ResourceTrimSettings.Default));
        Assert.DoesNotContain(
            "resourceTrim",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsInvalidResourceTrim()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "resourceTrim": { "keepRecent": 0 }
            }
            """);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => store.Load());

        Assert.Contains("at least 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsInvalidMicroCompaction()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "provider": "deepseek",
              "model": "deepseek-v4-flash",
              "microCompaction": { "protectTurns": 0 }
            }
            """);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => store.Load());

        Assert.Contains("at least 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_WritesProviderAndModelSelection()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
    public void Save_OmitsDisabledBuiltInTools()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithDisabledBuiltInTools(["describe_image", "bash"]);

        store.Save(settings);
        var json = File.ReadAllText(root.Home.ConfigPath);
        var loaded = store.Load();

        Assert.DoesNotContain("describe_image", json, StringComparison.Ordinal);
        Assert.DoesNotContain("disabledBuiltInTools", json, StringComparison.Ordinal);
        Assert.Empty(loaded.DisabledBuiltInTools.All());
    }

    [Fact]
    public void Save_RoundTripsPersistentDisabledBuiltInTools()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate()
            .WithPersistentDisabledBuiltInTools(["describe_image", "bash"]);

        store.Save(settings);
        var json = File.ReadAllText(root.Home.ConfigPath);
        var loaded = store.Load();

        Assert.Contains("disabledBuiltInTools", json, StringComparison.Ordinal);
        Assert.Equal(["describe_image", "bash"], loaded.PersistentDisabledBuiltInTools.Plan);
        Assert.Equal(loaded.PersistentDisabledBuiltInTools.Plan, loaded.DisabledBuiltInTools.Plan);
    }

    [Fact]
    public void Save_RoundTripsExportDirectory()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithExportDirectory("workspace");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("workspace", loaded.ExportDirectory);
    }

    [Fact]
    public void Save_RoundTripsHomeExportDirectory()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithExportDirectory("home");

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("home", loaded.ExportDirectory);
    }

    [Fact]
    public void Save_ClearsExportDirectoryWhenUnset()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
    public void Load_ReadsStatusLineRulesAndDropOrderFromHandEditedConfig()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.Save(store.LoadOrCreate());
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "customStatusLine": true,
              "statusLine": [
                "approval",
                { "field": "context-used", "keep": true },
                { "field": "model", "hideBelow": 100 },
                "elapsed"
              ],
              "statusLineDrop": ["elapsed"]
            }
            """);

        var loaded = store.Load().StatusLine;

        Assert.Equal(["approval", "context-used", "model", "elapsed"], loaded.Fields);
        Assert.Equal(
            [new StatusLineRule("context-used", true), new StatusLineRule("model", false, 100)],
            loaded.Rules);
        Assert.Equal(["elapsed"], loaded.DropOrder);
    }

    [Fact]
    public void Save_RoundTripsStatusLineRulesAndDropOrder()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithStatusLine(new StatusLineSettings(
            true, ["model", "elapsed"], [new("model", Keep: true)], ["elapsed"]));

        store.Save(settings);
        var loaded = store.Load().StatusLine;

        Assert.Equal([new StatusLineRule("model", true)], loaded.Rules);
        Assert.Equal(["elapsed"], loaded.DropOrder);
    }

    [Fact]
    public void Save_OmitsStatusLineEntriesThatAreDefault()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.Save(store.LoadOrCreate());

        var json = File.ReadAllText(root.Home.ConfigPath);

        Assert.DoesNotContain("statusLine", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsCustomStatusLine()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithStatusLine(
            new StatusLineSettings(true, ["context-left", "session-total"]));

        store.Save(settings);
        var loaded = store.Load();

        Assert.True(loaded.StatusLine.Enabled);
        Assert.Equal(["context-left", "session-total"], loaded.StatusLine.Fields);
    }

    [Fact]
    public void Save_RoundTripsAnUnloadedPluginStatusField()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithStatusLine(
            new StatusLineSettings(true, ["model", "branch"]));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(["model", "branch"], loaded.StatusLine.Fields);
    }

    [Fact]
    public void Save_RoundTripsAnEnabledApprovalModel()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);

        store.Save(HarnessSettings.CreateDefault());
        var loaded = store.Load();

        Assert.False(loaded.ApprovalModel.Enabled);
        Assert.False(loaded.ApprovalModel.HasSelection);
        Assert.DoesNotContain(
            "approvalModel",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "compactionModel",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_RoundTripsACompactionModelAndOmitsTheDefaultGear()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithCompactionModel(
            new CompactionModelSettings(true, "openai", "gpt-5.6-sol"));

        store.Save(settings);
        var loaded = store.Load();

        Assert.True(loaded.CompactionModel.Enabled);
        Assert.Equal("openai", loaded.CompactionModel.Provider);
        Assert.Equal("gpt-5.6-sol", loaded.CompactionModel.Model);
        Assert.Equal(ThinkingSelection.Default, loaded.CompactionModel.ThinkingEffort);
        Assert.DoesNotContain(
            "thinkingEffort",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);

        store.Save(loaded.WithCompactionModel(
            new CompactionModelSettings(
                false,
                "openai",
                "not-a-model",
                ThinkingSelection.Parse("off"))));
        var disabled = store.Load();

        Assert.False(disabled.CompactionModel.Enabled);
        Assert.Equal("not-a-model", disabled.CompactionModel.Model);
        Assert.Equal(ThinkingSelection.Off, disabled.CompactionModel.ThinkingEffort);
    }

    [Fact]
    public void Load_RejectsAnEnabledApprovalModelThatIsIncompleteOrUnknown()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
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
        var store = new DiskSettingsStore(root.Home);
        var settings = store.LoadOrCreate().WithResourceModel(
            ResourceKind.Image,
            new ResourceModelSettings(
                true,
                "openai",
                "gpt-5.6-sol",
                ThinkingSelection.Parse("high")));

        store.Save(settings);
        var loaded = store.Load();
        var json = File.ReadAllText(root.Home.ConfigPath);

        Assert.True(loaded.ResourceModels.Image.Enabled);
        Assert.Equal("openai", loaded.ResourceModels.Image.Provider);
        Assert.Equal("gpt-5.6-sol", loaded.ResourceModels.Image.Model);
        Assert.Equal("high", loaded.ResourceModels.Image.ThinkingEffort.Value);
        Assert.Contains("\"enabled\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"thinkingEffort\": \"high\"", json, StringComparison.Ordinal);

        store.Save(loaded.WithResourceModel(
            ResourceKind.Image,
            loaded.ResourceModels.Image.WithThinkingEffort(ThinkingSelection.Default)));
        var after = store.Load();

        Assert.Equal(ThinkingSelection.Default, after.ResourceModels.Image.ThinkingEffort);
        Assert.DoesNotContain(
            "thinkingEffort",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Save_OmitsAnUnsetImageModel()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);

        store.Save(HarnessSettings.CreateDefault());
        var loaded = store.Load();

        Assert.False(loaded.ResourceModels.Image.HasSelection);
        Assert.DoesNotContain(
            "imageModel",
            File.ReadAllText(root.Home.ConfigPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsAnImageModelThatIsIncompleteUnknownOrTextOnly()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "imageModel": { "provider": "openai" } }
            """);
        var incomplete = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal("imageModel requires provider and model together.", incomplete.Message);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "imageModel": { "enabled": true, "provider": "openai", "model": "not-a-model" } }
            """);
        var unknown = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Contains("not-a-model", unknown.Message, StringComparison.Ordinal);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "imageModel": { "enabled": true, "provider": "deepseek", "model": "deepseek-v4-pro" } }
            """);
        var textOnly = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Contains("does not accept image input", textOnly.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TopicModel_RoundTripsAndOmitsTheDefault()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.Save(store.LoadOrCreate());
        Assert.DoesNotContain("topicModel", File.ReadAllText(root.Home.ConfigPath), StringComparison.Ordinal);

        store.Save(store.Load().WithTopicModel(new TopicModelSettings(true, thinkingEffort: ThinkingSelection.Off)));
        var sessionModel = store.Load().TopicModel;
        Assert.True(sessionModel.Enabled);
        Assert.True(sessionModel.UsesSessionModel);
        Assert.Equal(ThinkingSelection.Off, sessionModel.ThinkingEffort);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "topicModel": { "enabled": true, "provider": "openai" } }
            """);
        var incomplete = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal(
            "topicModel requires provider and model together, or neither to use the session model.",
            incomplete.Message);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "topicModel": { "enabled": true, "provider": "openai", "model": "not-a-model" } }
            """);
        Assert.Contains("not-a-model", Assert.Throws<InvalidOperationException>(() => store.Load()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_TreatsAResourceModelWithoutEnabledAsOff()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ConfigPath,
            """
            {
              "imageModel": { "provider": "deepseek", "model": "deepseek-v4-pro" },
              "audioModel": { "provider": "openai", "model": "gpt-5.6-sol", "thinkingEffort": "high" }
            }
            """);

        var loaded = store.Load();

        Assert.False(loaded.ResourceModels.Image.Enabled);
        Assert.Equal("deepseek-v4-pro", loaded.ResourceModels.Image.Model);
        Assert.False(loaded.ResourceModels.Audio.Enabled);
        Assert.Equal("high", loaded.ResourceModels.Audio.ThinkingEffort.Value);

        store.Save(loaded);
        var json = File.ReadAllText(root.Home.ConfigPath);
        Assert.Contains("\"audioModel\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"enabled\": true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsAnEnabledAudioOrVideoModelWithoutThatInput()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "audioModel": { "enabled": true, "provider": "deepseek", "model": "deepseek-v4-pro" } }
            """);
        var audio = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal("Audio model 'deepseek-v4-pro' does not accept audio input.", audio.Message);

        File.WriteAllText(
            root.Home.ConfigPath,
            """
            { "videoModel": { "enabled": true } }
            """);
        var video = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal("videoModel.enabled requires provider and model.", video.Message);
    }
}
