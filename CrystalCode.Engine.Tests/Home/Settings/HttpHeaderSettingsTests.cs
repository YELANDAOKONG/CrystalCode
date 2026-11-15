using System.Text.Json;

using CrystalCode.Connections;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home.Settings;

using Xunit;

namespace CrystalCode.Engine.Tests.Home.Settings;

public sealed class HttpHeaderSettingsTests
{
    [Fact]
    public void LoadAndSave_MergesHeadersAndPreservesRemoval()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """
            { "provider": "local", "model": "test", "httpHeaders": {
              "X-Client": "client", "X-Region": "global", "X-Trace": "on",
              "User-Agent": "global/1.0", "Host": "gateway.example", "x-goog-api-key": "example" } }
            """);
        File.WriteAllText(root.Home.ProvidersPath, """
            { "local": { "protocol": "ollama", "baseUri": "http://localhost:11434/",
              "httpHeaders": { "x-region": "local", "x-trace": null, "user-agent": null, "host": "local.example" },
              "models": { "test": { "contextWindow": 10000 } } } }
            """);

        var settings = store.Load();
        Assert.Equal("client", settings.EffectiveHttpHeaders["x-client"]);
        Assert.Equal("local", settings.EffectiveHttpHeaders["X-Region"]);
        Assert.False(settings.EffectiveHttpHeaders.ContainsKey("X-Trace"));
        Assert.False(settings.EffectiveHttpHeaders.ContainsKey("User-Agent"));
        Assert.Equal("local.example", settings.EffectiveHttpHeaders["Host"]);
        Assert.Equal("example", settings.EffectiveHttpHeaders["x-goog-api-key"]);
        store.Save(settings.WithApproval(settings.Approval));
        Assert.Equal("local", store.Load().EffectiveHttpHeaders["X-REGION"]);
        var written = SettingsMapper.WriteProviders(settings.Catalog);
        var provider = SettingsMapper.ReadProviders(written).Single(p => p.Name.Value == "local");
        Assert.True(provider.HttpHeaders.ContainsKey("X-Trace"));
        Assert.Null(provider.HttpHeaders["X-Trace"]);
    }

    [Fact]
    public void CreateClient_ValidatesGlobalHeadersForTheSelectedProtocol()
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, """
            { "httpHeaders": { "X-Goog-Api-Key": "example" } }
            """);

        var settings = store.Load();
        using var client = Assert.IsAssignableFrom<IDisposable>(ChatClientFactory.Create(settings, "test-key"));
        var geminiModel = settings.Catalog.GetModelNames(ProviderNames.Gemini).First();
        var gemini = settings.WithSelection(ProviderNames.Gemini, geminiModel);
        Assert.Throws<InvalidOperationException>(() => ChatClientFactory.Create(gemini, "test-key"));
    }

    [Theory]
    [InlineData("{\"Proxy-Authorization\":\"secret\"}")]
    [InlineData("{\"Content-Type\":\"secret\"}")]
    [InlineData("{\"X-Test\":\"one\",\"x-test\":\"two\"}")]
    [InlineData("{\"X-Test\":\"secret\\r\\nvalue\"}")]
    [InlineData("{\"X-Test\":null}")]
    public void Load_RejectsInvalidGlobalHeadersWithoutValues(string headers)
    {
        using var root = new TemporaryHome();
        var store = new DiskSettingsStore(root.Home);
        store.LoadOrCreate();
        File.WriteAllText(root.Home.ConfigPath, $$"""{ "httpHeaders": {{headers}} }""");
        var error = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.DoesNotContain("secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderOverlay_MergesHeadersByName()
    {
        var original = new ProviderDefinition(new ProviderName("test"), ProviderProtocol.Ollama,
            new Uri("http://localhost/"), new Dictionary<string, ModelSettings>(),
            httpHeaders: new Dictionary<string, string?> { ["X-A"] = "a", ["X-B"] = "b" });
        var replacement = new ProviderDefinition(new ProviderName("test"), ProviderProtocol.Ollama,
            new Uri("http://localhost/"), new Dictionary<string, ModelSettings>(),
            httpHeaders: new Dictionary<string, string?> { ["x-b"] = null });
        var merged = new ProviderCatalog([original]).Overlay([replacement]).Get(new ProviderName("test")).Single();
        Assert.Equal("a", merged.HttpHeaders["x-a"]);
        Assert.Null(merged.HttpHeaders["X-B"]);
    }
}
