using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;

using Xunit;

namespace CrystalCode.Engine.Tests.Home;

public sealed class CredentialStoreTests
{
    [Fact]
    public void TryResolve_AllowsProviderWithoutApiKey()
    {
        using var root = new TemporaryHome();
        var store = new CredentialStore(root.Home);
        var provider = ProviderCatalog.CreateStarter().Get(ProviderName.Ollama)[0];

        var resolved = store.TryResolve(provider, out var apiKey, out var error);

        Assert.True(resolved);
        Assert.Equal(string.Empty, apiKey);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryResolve_ReadsFileByProviderName()
    {
        using var root = new TemporaryHome();
        root.Home.EnsureCreated();
        var store = new CredentialStore(root.Home);
        var name = new ProviderName("openrouter");
        store.Save(name, "or-key");
        var provider = new ProviderDefinition(
            name,
            ProviderProtocol.OpenAI,
            new Uri("https://openrouter.ai/api/v1/"),
            new Dictionary<string, ModelSettings>
            {
                ["m"] = new(1000)
            });

        var resolved = store.TryResolve(provider, out var apiKey, out var error);

        Assert.True(resolved);
        Assert.Equal("or-key", apiKey);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryResolve_ReadsLiteralApiKeyFromProvider()
    {
        using var root = new TemporaryHome();
        var store = new CredentialStore(root.Home);
        var provider = CreateProvider("sk-from-config");

        var resolved = store.TryResolve(provider, out var apiKey, out var error);

        Assert.True(resolved);
        Assert.Equal("sk-from-config", apiKey);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryResolve_ReadsApiKeyFromReferencedFile()
    {
        using var root = new TemporaryHome();
        root.Home.EnsureCreated();
        var keyPath = Path.Combine(root.Home.Root, "openrouter.key");
        File.WriteAllText(keyPath, "sk-from-file\n");
        var store = new CredentialStore(root.Home);
        var provider = CreateProvider("{file:openrouter.key}");

        var resolved = store.TryResolve(provider, out var apiKey, out var error);

        Assert.True(resolved);
        Assert.Equal("sk-from-file", apiKey);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryResolve_ReadsApiKeyFromEnvReference()
    {
        using var root = new TemporaryHome();
        const string variable = "CRYSTAL_TEST_OPENROUTER_KEY";
        Environment.SetEnvironmentVariable(variable, "sk-from-env");
        try
        {
            var store = new CredentialStore(root.Home);
            var provider = CreateProvider("{env:CRYSTAL_TEST_OPENROUTER_KEY}");

            var resolved = store.TryResolve(provider, out var apiKey, out var error);

            Assert.True(resolved);
            Assert.Equal("sk-from-env", apiKey);
            Assert.Equal(string.Empty, error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void TryResolve_MissingKeyErrorDoesNotPointAtOneProviderFile()
    {
        using var root = new TemporaryHome();
        var shared = Environment.GetEnvironmentVariable(CredentialStore.SharedApiKeyVariable);
        Environment.SetEnvironmentVariable(CredentialStore.SharedApiKeyVariable, null);
        try
        {
            var store = new CredentialStore(root.Home);
            var provider = new ProviderDefinition(
                new ProviderName("crystaltestnokey"),
                ProviderProtocol.OpenAI,
                new Uri("https://example.test/v1/"),
                new Dictionary<string, ModelSettings> { ["m"] = new(1000) });

            var resolved = store.TryResolve(provider, out _, out var error);

            Assert.False(resolved);
            Assert.Contains("Missing API key for crystaltestnokey", error, StringComparison.Ordinal);
            Assert.Contains("provider definition", error, StringComparison.Ordinal);
            Assert.DoesNotContain("providers.json", error, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CredentialStore.SharedApiKeyVariable, shared);
        }
    }

    private static ProviderDefinition CreateProvider(string apiKey) =>
        new(
            new ProviderName("openrouter"),
            ProviderProtocol.OpenAI,
            new Uri("https://openrouter.ai/api/v1/"),
            new Dictionary<string, ModelSettings>
            {
                ["m"] = new(1000)
            },
            apiKey: apiKey);
}
