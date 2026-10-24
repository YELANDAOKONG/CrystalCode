using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Providers;
using CrystalCode.Engine.Tools;
using CrystalCode.Providers.Anthropic;
using CrystalCode.Providers.Gemini;
using CrystalCode.Providers.Ollama;
using CrystalCode.Providers.OpenAI;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Engine.Tests.Configuration;

public sealed class ChatClientFactoryTests
{
    [Fact]
    public void ResponsesFactory_OffersApplyPatchAsTextOnlyForAModelThatDeclaredIt()
    {
        var declared = ResponsesClientFactory.TextToolsFor(new ModelSettings(8192, textTools: true));
        var plain = ResponsesClientFactory.TextToolsFor(new ModelSettings(8192));

        var binding = Assert.Single(declared);
        Assert.Equal("apply_patch", binding.ToolName);
        Assert.Equal("patchText", binding.Property);
        Assert.Empty(plain);
    }

    [Fact]
    public async Task ResponsesFactory_BindingMatchesTheRealApplyPatchDefinition()
    {
        // The binding is matched against the tool's own schema. If that schema
        // ever grew a second property, the binding would silently stop matching
        // and the model would be offered a function tool again.
        using var root = new Tools.TemporaryWorkspace();
        var tool = new ApplyPatchTool(new Workspace(root.Path));
        var binding = Assert.Single(ResponsesClientFactory.TextToolsFor(new ModelSettings(8192, textTools: true)));
        var handler = new CapturingHandler(
            """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}""");
        using var http = new HttpClient(handler);
        using var provider = new ResponsesProvider(
            new ResponsesOptions("key", "m", new Uri("https://example.test/"), textTools: [binding]),
            http);

        await provider.CompleteAsync(new Crystal.Chat.ChatRequest(
            [new Crystal.Chat.ChatMessage(Crystal.Chat.ChatRole.User, "go")],
            [tool.Definition]));

        using var sent = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal("custom", sent.RootElement.GetProperty("tools")[0].GetProperty("type").GetString());
    }

    private sealed class CapturingHandler(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public void Create_UsesNativeAdaptersAndKeylessOllama()
    {
        var defaults = HarnessSettings.CreateDefault();
        var gemini = defaults.WithSelection(ProviderName.Gemini, "gemini-3.8-flash");
        var ollama = defaults.WithSelection(ProviderName.Ollama, "qwen3:8b");

        using var geminiClient = Assert.IsType<GeminiProvider>(
            ChatClientFactory.Create(gemini, "test-key"));
        using var ollamaClient = Assert.IsType<OllamaProvider>(
            ChatClientFactory.Create(ollama, string.Empty));
        using var geminiImages = Assert.IsType<GeminiMultimodalProvider>(
            MultimodalChatClientFactory.Create(gemini, "test-key"));
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    public void CreateMultimodal_UsesConfiguredImageAdapter(string protocolText)
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
                    ["model"] = new(200000, imageInput: true)
                })
        ]);
        var settings = new HarnessSettings(
            new ProviderName("gateway"),
            "model",
            ApprovalMode.Default,
            0.8,
            catalog);

        var client = MultimodalChatClientFactory.Create(settings, "test-key");
        try
        {
            if (protocol == ProviderProtocol.OpenAI)
            {
                Assert.IsType<OpenAIMultimodalProvider>(client);
            }
            else
            {
                Assert.IsType<AnthropicMultimodalProvider>(client);
            }
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }
    }

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
