using CrystalHarness.Approvals;
using CrystalHarness.Configuration;
using CrystalHarness.Providers.OpenAI;

using Xunit;

namespace CrystalHarness.Tests.Configuration;

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
}
