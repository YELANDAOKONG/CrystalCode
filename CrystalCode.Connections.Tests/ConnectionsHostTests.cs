using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;

using Xunit;

namespace CrystalCode.Connections.Tests;

public sealed class ConnectionsHostTests
{
    [Fact]
    public void BuiltInRegistry_ServesEveryBuiltInProtocol()
    {
        var registry = PluginRegistry.CreateBuiltIn();

        Assert.Equal(6, registry.Clients.Count);
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.DeepSeek));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.OpenAI));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.Responses));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.Anthropic));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.Gemini));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.Ollama));
    }

    [Fact]
    public void Registration_ProvidesTheStarterCatalogAndDefaultSelection()
    {
        var catalog = HostDefaults.CreateCatalog();
        var (provider, model) = HostDefaults.GetDefaultSelection();

        Assert.Equal(ProviderNames.DeepSeek, provider);
        Assert.True(catalog.Get(provider)[0].TryGetModel(model, out _));
        Assert.Equal(["deepseek", "gemini", "ollama", "openai"], catalog.Providers.Keys.Order());
    }

    [Fact]
    public void Registering_ReplacesTheSameKey()
    {
        ConnectionsHost.Register();
        ConnectionsHost.Register();

        Assert.Equal(6, PluginRegistry.CreateBuiltIn().Clients.Count);
    }
}
