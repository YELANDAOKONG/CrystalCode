using CrystalCode.Configuration;
using CrystalCode.Home;

using Xunit;

namespace CrystalCode.Tests.Home;

public sealed class CredentialStoreTests
{
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
