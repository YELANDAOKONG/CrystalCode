using CrystalCode.Approvals;
using CrystalCode.Configuration;
using CrystalCode.Providers.Anthropic;
using CrystalCode.Providers.OpenAI;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Tests.Configuration;

public sealed class ChatClientFactoryTests
{
    [Fact]
    public void Create_UsesOpenAIAdapterForCompatibleProtocol()
    {
        var catalog = ProviderCatalog.CreateStarter().Overlay(
        [
            new ProviderDefinition(
                new ProviderName("groq"),
                ProviderProtocol.OpenAI,
                new Uri("https://api.groq.com/openai/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["llama"] = new(131072, temperature: 0.1, maxTokens: 1024)
                },
                tokenLimit: TokenLimitStyle.MaxTokens)
        ]);
        var settings = new HarnessSettings(
            new ProviderName("groq"),
            "llama",
            ApprovalMode.Default,
            0.8,
            catalog);

        var client = ChatClientFactory.Create(settings, "test-key");
        try
        {
            Assert.IsType<OpenAIProvider>(client);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("responses")]
    [InlineData("anthropic")]
    public void Create_UsesConfiguredWireAdapter(string protocolText)
    {
        var protocol = ProviderProtocol.Parse(protocolText);
        var catalog = ProviderCatalog.CreateStarter().Overlay(
        [
            new ProviderDefinition(
                new ProviderName("gateway"),
                protocol,
                new Uri("https://example.test/v1/"),
                new Dictionary<string, ModelSettings>
                {
                    ["model"] = new(200000, maxTokens: 4096)
                })
        ]);
        var settings = new HarnessSettings(
            new ProviderName("gateway"),
            "model",
            ApprovalMode.Default,
            0.8,
            catalog);

        var client = ChatClientFactory.Create(settings, "test-key");
        try
        {
            if (protocol == ProviderProtocol.Responses)
            {
                Assert.IsType<ResponsesProvider>(client);
            }
            else
            {
                Assert.IsType<AnthropicProvider>(client);
            }
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }
}
