using Crystal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Connections.Anthropic;
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

    [Fact]
    public void FactoryWithoutHandlerSupport_RefusesAnAccountLogin()
    {
        var credential = new ProviderCredential(string.Empty, new MarkingHandler());
        IChatClientFactory factory = new AnthropicClientFactory();

        Assert.Throws<NotSupportedException>(() => factory.Create(AccountSettings(), credential));
        Assert.Throws<NotSupportedException>(() => factory.CreateMultimodal(AccountSettings(), credential));
    }

    private static HarnessSettings AccountSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("account"),
            ProviderProtocol.Responses,
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Header = request.Headers.TryGetValues("X-Marked", out var values) ? values.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
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
