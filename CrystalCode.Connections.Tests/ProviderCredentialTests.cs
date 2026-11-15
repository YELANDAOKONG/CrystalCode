using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Plugins;
using CrystalCode.Connections.Responses;

using Xunit;

namespace CrystalCode.Connections.Tests;

public sealed class ProviderCredentialTests
{
    [Fact]
    public async Task ResponsesFactory_SendsRequestsThroughTheIntegrationHandler()
    {
        var capture = new CaptureInner();
        var credential = new ProviderCredential(string.Empty, new MarkingHandler { InnerHandler = capture });
        var settings = AccountSettings();

        using var client = (IDisposable)new ResponsesClientFactory().Create(settings, credential);
        await ((IChatClient)client).CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "go")]));

        Assert.Equal("marked", capture.Header);
        Assert.Null(capture.Authorization);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("deepseek")]
    [InlineData("anthropic")]
    [InlineData("gemini")]
    [InlineData("ollama")]
    public async Task EveryBuiltInFactory_SendsThroughTheHandlerWithoutItsOwnAuthHeader(string protocol)
    {
        var capture = new CaptureInner();
        var credential = new ProviderCredential(string.Empty, new MarkingHandler { InnerHandler = capture });
        var factory = PluginRegistry.CreateBuiltIn().Clients.Single(client => client.CanCreate(ProviderProtocol.Parse(protocol)));

        using var text = (IDisposable)factory.Create(AccountSettings(protocol), credential);
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await ((IChatClient)text).CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "go")])));

        Assert.Equal("marked", capture.Header);
        Assert.Null(capture.Authorization);
        Assert.False(capture.HasVendorKeyHeader);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("deepseek")]
    [InlineData("anthropic")]
    [InlineData("gemini")]
    [InlineData("ollama")]
    public async Task EveryBuiltInMultimodalFactory_SendsThroughTheHandler(string protocol)
    {
        var capture = new CaptureInner();
        var credential = new ProviderCredential(string.Empty, new MarkingHandler { InnerHandler = capture });
        var factory = PluginRegistry.CreateBuiltIn().Clients.Single(client => client.CanCreate(ProviderProtocol.Parse(protocol)));

        var client = factory.CreateMultimodal(AccountSettings(protocol), credential);

        Assert.NotNull(client);
        using var disposable = (IDisposable)client;
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await client.CompleteAsync(
                new MultimodalChatRequest([new MultimodalMessage(MultimodalChatRole.User, [new TextContent("go")])])));
        Assert.Equal("marked", capture.Header);
        Assert.False(capture.HasVendorKeyHeader);
    }

    [Fact]
    public void AFactoryWithoutHandlerSupport_RefusesAnAccountLogin()
    {
        var credential = new ProviderCredential(string.Empty, new MarkingHandler());
        IChatClientFactory factory = new PlainFactory();

        Assert.Throws<NotSupportedException>(() => factory.Create(AccountSettings(), credential));
        Assert.Throws<NotSupportedException>(() => factory.CreateMultimodal(AccountSettings(), credential));
    }

    private sealed class PlainFactory : IChatClientFactory
    {
        public bool CanCreate(ProviderProtocol protocol) => true;

        public IStreamingChatClient Create(HarnessSettings settings, string apiKey) =>
            throw new InvalidOperationException("The string overload must not be reached.");
    }

    private static HarnessSettings AccountSettings(string protocol = "responses")
    {
        var provider = new ProviderDefinition(
            new ProviderName("account"),
            ProviderProtocol.Parse(protocol),
            new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["m"] = new(64000) },
            type: ProviderType.Authentication,
            integration: "example");
        return new HarnessSettings(
            provider.Name,
            "m",
            CrystalCode.Engine.Approvals.ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold,
            new ProviderCatalog([provider]));
    }

    private sealed class MarkingHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("X-Marked", "marked");
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class CaptureInner : HttpMessageHandler
    {
        public string? Header { get; private set; }

        public string? Authorization { get; private set; }

        public bool HasVendorKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Header = request.Headers.TryGetValues("X-Marked", out var values) ? values.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            HasVendorKeyHeader = request.Headers.Contains("x-api-key") || request.Headers.Contains("x-goog-api-key");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
