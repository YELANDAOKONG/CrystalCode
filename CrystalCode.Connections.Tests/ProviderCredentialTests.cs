using System.Text.Json;

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
    [Theory]
    [InlineData(false, null)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ResponsesFactory_WritesAccountInstructionsThroughTheIntegrationHandler(
        bool multimodal,
        bool? systemAsDeveloper)
    {
        var capture = new CaptureInner();
        var credential = new ProviderCredential(string.Empty, new MarkingHandler { InnerHandler = capture });
        var settings = AccountSettings(responses: new ResponsesRequestSettings(SystemAsDeveloper: systemAsDeveloper));
        var factory = new ResponsesClientFactory();

        if (multimodal)
        {
            var client = factory.CreateMultimodal(settings, credential);
            using var disposable = (IDisposable)client;
            await client.CompleteAsync(new MultimodalChatRequest(
            [
                new MultimodalMessage(MultimodalChatRole.System, [new TextContent("workspace rules")]),
                new MultimodalMessage(MultimodalChatRole.User, [new TextContent("go")])
            ]));
        }
        else
        {
            var client = factory.Create(settings, credential);
            using var disposable = (IDisposable)client;
            await client.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.System, "workspace rules"),
                new ChatMessage(ChatRole.User, "go")
            ]));
        }

        Assert.Equal("marked", capture.Header);
        Assert.Null(capture.Authorization);

        using var body = JsonDocument.Parse(Assert.IsType<string>(capture.Body));
        var root = body.RootElement;
        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal("user", input[^1].GetProperty("role").GetString());
        Assert.False(root.TryGetProperty("max_output_tokens", out _));
        if (systemAsDeveloper == true)
        {
            Assert.False(root.TryGetProperty("instructions", out _));
            Assert.Equal(2, input.Length);
            Assert.Equal("developer", input[0].GetProperty("role").GetString());
            var content = input[0].GetProperty("content");
            Assert.Equal("workspace rules", multimodal ? content[0].GetProperty("text").GetString() : content.GetString());
        }
        else
        {
            Assert.Single(input);
            Assert.Equal("workspace rules", root.GetProperty("instructions").GetString());
        }
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

    private static HarnessSettings AccountSettings(
        string protocol = "responses",
        ResponsesRequestSettings? responses = null)
    {
        var provider = new ProviderDefinition(
            new ProviderName("account"),
            ProviderProtocol.Parse(protocol),
            new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["m"] = new(64000, maxTokens: 4096, responses: responses) },
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

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Header = request.Headers.TryGetValues("X-Marked", out var values) ? values.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            HasVendorKeyHeader = request.Headers.Contains("x-api-key") || request.Headers.Contains("x-goog-api-key");
            Body = request.Content is { } content ? await content.ReadAsStringAsync(cancellationToken) : null;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}]}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
