using System.Text.Json;

using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Plugins;
using CrystalCode.Connections.Responses;
using CrystalCode.Plugins.Clients;
using CrystalCode.Providers.Responses;

using Xunit;

namespace CrystalCode.Connections.Tests;

public sealed class ProviderCredentialTests
{
    [Theory]
    [InlineData(false, false, null, false)]
    [InlineData(false, true, null, true)]
    [InlineData(true, true, null, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, true)]
    public async Task ResponsesFactory_WritesAccountInstructionsThroughTheIntegrationHandler(
        bool multimodal,
        bool officialEndpoint,
        bool? systemAsDeveloper,
        bool expectDeveloper)
    {
        var capture = new CaptureInner();
        var credential = new ProviderCredential(string.Empty, new MarkingHandler { InnerHandler = capture });
        var settings = AccountSettings(
            responses: new ResponsesRequestSettings(SystemAsDeveloper: systemAsDeveloper),
            baseUri: officialEndpoint ? new Uri("https://api.openai.com/v1/") : null,
            integration: "chatgpt");
        var factory = new ResponsesClientFactory();
        var observed = new List<ProviderRequestUsage>();
        ProviderRequestContext Context() => new("child", "parent", observed.Add);

        if (multimodal)
        {
            var client = factory.CreateMultimodal(settings, credential, Context);
            using var disposable = (IDisposable)client;
            var response = await client.CompleteAsync(new MultimodalChatRequest(
            [
                new MultimodalMessage(MultimodalChatRole.System, [new TextContent("workspace rules")]),
                new MultimodalMessage(MultimodalChatRole.User, [new TextContent("go")])
            ]));
            var message = Assert.IsType<MultimodalMessage>(Assert.Single(Assert.Single(response.Candidates).Items));
            Assert.Equal("ok", Assert.IsType<TextContent>(Assert.Single(message.Contents)).Text);
        }
        else
        {
            var client = factory.Create(settings, credential, Context);
            using var disposable = (IDisposable)client;
            var response = await client.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.System, "workspace rules"),
                new ChatMessage(ChatRole.User, "go")
            ]));
            Assert.Equal("ok", Assert.IsType<ChatMessage>(Assert.Single(Assert.Single(response.Candidates).Items)).Text);
        }

        Assert.Equal("marked", capture.Header);
        Assert.Null(capture.Authorization);
        Assert.Equal(officialEndpoint ? "parent" : null, capture.Affinity);
        Assert.Equal(officialEndpoint, capture.Thread is not null);
        Assert.Equal(officialEndpoint, Guid.TryParseExact(capture.ClientRequestId, "N", out _));
        Assert.Equal(new ProviderRequestUsage(1, 1200, 5, 2, 1024, 100), Assert.Single(observed));

        using var body = JsonDocument.Parse(Assert.IsType<string>(capture.Body));
        var root = body.RootElement;
        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal("user", input[^1].GetProperty("role").GetString());
        Assert.False(root.TryGetProperty("max_output_tokens", out _));
        Assert.Equal(officialEndpoint, root.GetProperty("stream").GetBoolean());
        Assert.False(root.TryGetProperty("prompt_cache_key", out _));
        if (expectDeveloper)
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponsesFactory_RoutesAnInheritedChildWithinItsOwnBranch(bool auxiliary)
    {
        var capture = new CaptureInner();
        var credential = new ProviderCredential(string.Empty, new MarkingHandler { InnerHandler = capture });
        var settings = AccountSettings(
            responses: new ResponsesRequestSettings(PromptCacheKey: true),
            baseUri: new Uri("https://api.openai.com/v1/"),
            integration: "chatgpt");
        var parent = "original";
        var request = new ChatRequest([new ChatMessage(ChatRole.User, "go")]);
        var client = new ResponsesClientFactory().Create(settings, credential,
            () => new ProviderRequestContext("inherited-child", parent, _ => { }, IsAuxiliary: auxiliary));
        using var disposable = (IDisposable)client;

        async Task<(string Thread, string Affinity)> SendAsync()
        {
            await client.CompleteAsync(request);
            var thread = Assert.IsType<string>(capture.Thread);
            var affinity = Assert.IsType<string>(capture.Affinity);
            using var body = JsonDocument.Parse(Assert.IsType<string>(capture.Body));
            Assert.Equal(affinity, body.RootElement.GetProperty("prompt_cache_key").GetString());
            Assert.Equal(auxiliary ? thread : parent, affinity);
            return (thread, affinity);
        }

        var original = await SendAsync();
        parent = "fork";
        var fork = await SendAsync();
        Assert.NotEqual(original.Thread, fork.Thread);
        Assert.NotEqual(original.Affinity, fork.Affinity);
        Assert.Equal(fork, await SendAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ResponsesFactory_RecordsOnlyInferenceAttemptsOnAuthenticationFailure(int attempts)
    {
        var capture = new CaptureInner();
        var handler = new MarkingHandler
        {
            InnerHandler = capture,
            FailAfterInferenceAttempts = attempts
        };
        var credential = new ProviderCredential(string.Empty, handler);
        var settings = AccountSettings(
            baseUri: new Uri("https://api.openai.com/v1/"),
            integration: "chatgpt");
        var observed = new List<ProviderRequestUsage>();
        var client = new ResponsesClientFactory().Create(settings, credential,
            () => new ProviderRequestContext("parent", null, observed.Add));
        using var disposable = (IDisposable)client;

        var error = await Assert.ThrowsAsync<ResponsesException>(() =>
            client.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "go")])));
        Assert.IsType<AuthenticationFailedException>(error.InnerException);
        if (attempts == 0)
        {
            Assert.Empty(observed);
        }
        else
        {
            Assert.Equal(new ProviderRequestUsage(attempts), Assert.Single(observed));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResponsesFactory_UsesCurrentContextAndAllowsOnlyTheCacheKey(
        bool account)
    {
        var capture = new CaptureInner();
        var credential = new ProviderCredential(string.Empty, new MarkingHandler { InnerHandler = capture });
        var settings = AccountSettings(
            responses: new ResponsesRequestSettings(SessionAffinity: false, PromptCacheKey: true),
            baseUri: new Uri(account ? "https://api.openai.com/v1/" : "https://example.test/v1/"),
            integration: "chatgpt");
        var thread = "first";
        var request = new ChatRequest([new ChatMessage(ChatRole.User, "go")]);
        var client = new ResponsesClientFactory().Create(settings, credential, () => new(thread, null, _ => { }));
        using var disposable = (IDisposable)client;

        foreach (var expected in new[] { "first", "resumed" })
        {
            thread = expected;
            await client.CompleteAsync(request);
            using var body = JsonDocument.Parse(Assert.IsType<string>(capture.Body));
            var hasKey = body.RootElement.TryGetProperty("prompt_cache_key", out var key);
            Assert.Equal(account, hasKey);
            if (hasKey)
            {
                Assert.Equal(expected, key.GetString());
            }
            Assert.Null(capture.Affinity);
            Assert.Null(capture.Thread);
        }
    }

    [Theory]
    [InlineData("https://api.openai.com/v1/", null, null, true)]
    [InlineData("https://api.openai.com/v1", null, null, true)]
    [InlineData("https://example.test/v1/", null, null, false)]
    [InlineData("https://api.openai.com/v1/", true, null, false)]
    [InlineData("https://api.openai.com/v1/", false, null, false)]
    [InlineData("https://api.openai.com/v1/", null, false, false)]
    [InlineData("https://api.openai.com/v1/", true, true, true)]
    public async Task ResponsesFactory_ResolvesDeveloperDefaultForApiKey(
        string endpoint,
        bool? systemAsInstructions,
        bool? systemAsDeveloper,
        bool expected)
    {
        var provider = new ProviderDefinition(
            new ProviderName("api"),
            ProviderProtocol.Responses,
            new Uri(endpoint),
            new Dictionary<string, ModelSettings>
            {
                ["m"] = new(64000, responses: new ResponsesRequestSettings(
                    SystemAsInstructions: systemAsInstructions,
                    SystemAsDeveloper: systemAsDeveloper,
                    SessionAffinity: true,
                    PromptCacheKey: true))
            });
        var settings = new HarnessSettings(
            provider.Name,
            "m",
            CrystalCode.Engine.Approvals.ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold,
            new ProviderCatalog([provider]));

        var options = ResponsesClientFactory.CreateOptions(settings, new ProviderCredential("test-key"));
        var shape = options.RequestShape;

        Assert.Equal(expected, shape.SystemAsDeveloper);
        Assert.Equal(systemAsInstructions ?? false, shape.SystemAsInstructions);
        Assert.False(shape.OmitSampling);
        var capture = new CaptureInner();
        using var http = new HttpClient(capture);
        var usage = new List<ProviderRequestUsage>();
        using var client = new ResponsesProvider(options with { RequestContext = () => new("main", null, usage.Add) }, http);
        await client.CompleteAsync(new ChatRequest([new ChatMessage(ChatRole.User, "hello")]));

        using var body = JsonDocument.Parse(Assert.IsType<string>(capture.Body));
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("prompt_cache_key", out _));
        Assert.Null(capture.Affinity);
        Assert.Null(capture.Thread);
        Assert.Null(capture.ClientRequestId);
        Assert.Equal(1024, Assert.Single(usage).CacheReadTokens);
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
        ResponsesRequestSettings? responses = null,
        Uri? baseUri = null,
        string integration = "example")
    {
        var provider = new ProviderDefinition(
            new ProviderName("account"),
            ProviderProtocol.Parse(protocol),
            baseUri ?? new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["m"] = new(64000, maxTokens: 4096, responses: responses) },
            type: ProviderType.Authentication,
            integration: integration);
        return new HarnessSettings(
            provider.Name,
            "m",
            CrystalCode.Engine.Approvals.ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold,
            new ProviderCatalog([provider]));
    }

    private sealed class MarkingHandler : DelegatingHandler
    {
        public int? FailAfterInferenceAttempts { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (FailAfterInferenceAttempts is { } attempts)
            {
                request.Options.Set(ProviderHttpRequestOptions.InferenceAttempts, attempts);
                return Task.FromException<HttpResponseMessage>(
                    new AuthenticationFailedException("The test token could not be renewed."));
            }

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

        public string? Affinity { get; private set; }
        public string? Thread { get; private set; }
        public string? ClientRequestId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Header = request.Headers.TryGetValues("X-Marked", out var values) ? values.Single() : null;
            Authorization = request.Headers.Authorization?.ToString();
            HasVendorKeyHeader = request.Headers.Contains("x-api-key") || request.Headers.Contains("x-goog-api-key");
            Body = request.Content is { } content ? await content.ReadAsStringAsync(cancellationToken) : null;
            Affinity = HeaderValue(request, "session-id");
            Thread = HeaderValue(request, "thread-id");
            ClientRequestId = HeaderValue(request, "x-client-request-id");
            const string response = """
                {"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":1200,"output_tokens":5,"input_tokens_details":{"cached_tokens":1024,"cache_write_tokens":100},"output_tokens_details":{"reasoning_tokens":2}}}
                """;
            var stream = request.Headers.Accept.Any(value => value.MediaType == "text/event-stream");
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    stream
                        ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"o\"}\n\n"
                            + """data: {"type":"response.completed","response":""" + response + "}\n\n"
                        : response,
                    System.Text.Encoding.UTF8,
                    stream ? "text/event-stream" : "application/json")
            };
        }

        private static string? HeaderValue(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
