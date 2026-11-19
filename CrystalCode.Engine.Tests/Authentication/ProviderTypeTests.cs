using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home.Settings;
using CrystalCode.Engine.Tests.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Authentication;

public sealed class ProviderTypeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    [InlineData("key")]
    public void Parse_TreatsOmittedDefaultAndKeyAsKey(string? text)
    {
        Assert.Equal(ProviderType.Key, ProviderTypeText.Parse("p", text));
    }

    [Fact]
    public void Parse_ReadsAuthenticationAndRejectsUnknown()
    {
        Assert.Equal(ProviderType.Authentication, ProviderTypeText.Parse("p", "Authentication"));
        var error = Assert.Throws<InvalidOperationException>(() => ProviderTypeText.Parse("p", "oauth"));
        Assert.Contains("'oauth'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Definition_RequiresIntegrationAndModelsForAuthentication()
    {
        var models = new Dictionary<string, ModelSettings> { ["m"] = new(1000) };
        var baseUri = new Uri("https://example.test/");

        Assert.Throws<ArgumentException>(() => new ProviderDefinition(
            ProviderName.Parse("p"), ProviderProtocol.Responses, baseUri, models,
            type: ProviderType.Authentication));
        Assert.Throws<ArgumentException>(() => new ProviderDefinition(
            ProviderName.Parse("p"), ProviderProtocol.Responses, baseUri,
            new Dictionary<string, ModelSettings>(),
            type: ProviderType.Authentication, integration: "x"));
        Assert.Throws<ArgumentException>(() => new ProviderDefinition(
            ProviderName.Parse("p"), ProviderProtocol.Responses, baseUri, models,
            integration: "x"));
    }

    [Fact]
    public void Load_ReadsAuthenticationProviderAndNeedsNoApiKey()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              "account": {
                "type": "authentication",
                "integration": "example",
                "protocol": "responses",
                "baseUri": "https://example.test/v1/",
                "models": { "model-a": { "contextWindow": 64000 } }
              }
            }
            """);

        var provider = store.Load().Catalog.Get(ProviderName.Parse("account"))[0];

        Assert.Equal(ProviderType.Authentication, provider.Type);
        Assert.Equal("example", provider.Integration);
        Assert.False(provider.RequiresApiKey);
    }

    [Fact]
    public void Load_ReadsAndRoundTripsTheResponsesObject()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              "account": {
                "protocol": "responses",
                "baseUri": "https://example.test/v1/",
                "models": {
                  "model-a": {
                    "contextWindow": 64000,
                    "responses": {
                      "systemAsInstructions": true, "omitSampling": false, "systemAsDeveloper": true,
                      "sessionAffinity": false, "promptCacheKey": true
                    }
                  }
                }
              }
            }
            """);

        var catalog = store.Load().Catalog;
        var shape = catalog.Get(ProviderName.Parse("account"))[0].Models["model-a"].Responses;

        Assert.Equal(new ResponsesRequestSettings(true, null, false,
            SystemAsDeveloper: true, SessionAffinity: false, PromptCacheKey: true), shape);
        var written = System.Text.Json.JsonSerializer.Serialize(SettingsMapper.WriteProviders(catalog));
        Assert.Contains("\"responses\"", written, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"systemAsDeveloper\":true", written, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sessionAffinity\":false", written, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"promptCacheKey\":true", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("includeEncryptedReasoning", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsAResponsesObjectOnAnotherProtocol()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              "other": {
                "protocol": "openai",
                "baseUri": "https://example.test/v1/",
                "models": { "m": { "contextWindow": 1000, "responses": { "omitSampling": true } } }
              }
            }
            """);

        var error = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Contains("only the responses protocol", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RejectsUnknownType()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(
            root.Home.ProvidersPath,
            """
            {
              "account": {
                "type": "oauth",
                "protocol": "responses",
                "baseUri": "https://example.test/v1/",
                "models": { "model-a": { "contextWindow": 64000 } }
              }
            }
            """);

        Assert.Throws<InvalidOperationException>(() => store.Load());
    }
}
