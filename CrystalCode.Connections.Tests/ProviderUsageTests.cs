using System.Net;
using System.Text;

using Crystal.Chat;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Authentication;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;
using CrystalCode.Plugins.Clients;

using Xunit;

namespace CrystalCode.Connections.Tests;

public sealed class ProviderUsageTests
{
    public static TheoryData<string, bool, bool> Requests()
    {
        var cases = new TheoryData<string, bool, bool>();
        foreach (var protocol in new[] { "openai", "deepseek", "responses", "anthropic", "gemini", "ollama" })
        {
            cases.Add(protocol, false, false);
            cases.Add(protocol, false, true);
            cases.Add(protocol, true, false);
            cases.Add(protocol, true, true);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task BuiltInFactory_CollectsNativeUsage(string protocol, bool stream, bool multimodal)
    {
        var (body, contentType) = ResponseFor(protocol, stream);
        var usage = Assert.Single(await SendAsync(protocol, stream, multimodal, body, contentType));

        Assert.Equal(1, usage.HttpAttempts);
        Assert.Equal(100, usage.InputTokens);
        Assert.Equal(12, usage.OutputTokens);
        Assert.Equal(protocol == "ollama" ? 0 : 60, usage.CacheReadTokens);
        Assert.Equal(protocol is "anthropic" or "responses" ? 25 : null, usage.CacheWriteTokens);
        Assert.Equal(protocol switch { "anthropic" => 15, "deepseek" => 40, _ => (long?)null }, usage.UncachedInputTokens);
        Assert.Equal(protocol is "anthropic" or "ollama" ? null : 2, usage.ReasoningTokens);
        switch (protocol)
        {
            case "anthropic":
                Assert.Equal(13, usage.CacheWritesByLifetime![300]);
                Assert.Equal(12, usage.CacheWritesByLifetime[3600]);
                break;
            case "responses":
                Assert.Equal(0, usage.CacheWritesByLifetime![1800]);
                Assert.Equal(25, usage.CacheWritesByLifetime[43200]);
                break;
            default:
                Assert.Null(usage.CacheWritesByLifetime);
                break;
        }
    }

    [Fact]
    public async Task BuiltInFactory_LeavesUnreportedCacheCountsUnknown()
    {
        const string response = """
            {"message":{"role":"assistant","content":"ok"},"done":true,"done_reason":"stop","prompt_eval_count":100,"eval_count":12}
            """;
        var usage = Assert.Single(await SendAsync("ollama", false, false, response, "application/json"));
        Assert.Equal(100, usage.InputTokens);
        Assert.Null(usage.CacheReadTokens);
        Assert.Null(usage.CacheWriteTokens);
    }

    private static async Task<IReadOnlyList<ProviderRequestUsage>> SendAsync(
        string protocol, bool stream, bool multimodal, string body, string contentType)
    {
        var provider = new ProviderDefinition(
            new ProviderName("usage-test"), ProviderProtocol.Parse(protocol), new Uri("https://example.test/v1/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(64000, imageInput: true) },
            type: ProviderType.Authentication, integration: "test");
        var settings = new HarnessSettings(provider.Name, "model", ApprovalMode.Default,
            HarnessSettings.DefaultCompactionThreshold, new ProviderCatalog([provider]));
        var credential = new ProviderCredential(string.Empty,
            new UsageHttpHandler { InnerHandler = new UsageResponseHandler(body, contentType) });
        var factory = PluginRegistry.CreateBuiltIn().Clients.Single(item => item.CanCreate(provider.Protocol));
        var reports = new List<ProviderRequestUsage>();
        ProviderRequestContext Capture() => new("session", null, reports.Add);

        if (multimodal)
        {
            var client = factory.CreateMultimodal(settings, credential, Capture)!;
            using var disposable = (IDisposable)client;
            var request = new MultimodalChatRequest(
                [new MultimodalMessage(MultimodalChatRole.User, [new TextContent("Hi")])]);
            if (stream)
            {
                await foreach (var _ in client.StreamAsync(request))
                {
                }
            }
            else
            {
                await client.CompleteAsync(request);
            }
        }
        else
        {
            var client = factory.Create(settings, credential, Capture);
            using var disposable = (IDisposable)client;
            var request = new ChatRequest([new ChatMessage(ChatRole.User, "Hi")]);
            if (stream)
            {
                await foreach (var _ in client.StreamAsync(request))
                {
                }
            }
            else
            {
                await client.CompleteAsync(request);
            }
        }

        return reports;
    }

    private static (string Body, string ContentType) ResponseFor(string protocol, bool stream)
    {
        const string json = "application/json";
        const string events = "text/event-stream";
        switch (protocol)
        {
            case "openai":
            case "deepseek":
                var cache = protocol == "deepseek"
                    ? """
                        "prompt_cache_hit_tokens":60,"prompt_cache_miss_tokens":40
                        """
                    : """
                        "prompt_tokens_details":{"cached_tokens":60}
                        """;
                var usage = $$"""{"prompt_tokens":100,"completion_tokens":12,"completion_tokens_details":{"reasoning_tokens":2},{{cache}}}""";
                return stream
                    ? (Sse(
                        """{"choices":[{"index":0,"delta":{"content":"ok"},"finish_reason":"stop"}]}""",
                        $$"""{"choices":[],"usage":{{usage}}}""",
                        "[DONE]"), events)
                    : ($$"""{"choices":[{"index":0,"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{{usage}}}""", json);
            case "anthropic":
                const string anthropicInput = """
                    "input_tokens":15,"cache_read_input_tokens":60,"cache_creation_input_tokens":25,"cache_creation":{"ephemeral_5m_input_tokens":13,"ephemeral_1h_input_tokens":12}
                    """;
                return stream
                    ? (Sse(
                        $$"""{"type":"message_start","message":{"content":[],"usage":{ {{anthropicInput}},"output_tokens":1} } }""",
                        """{"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":12} }""",
                        """{"type":"message_stop"}"""), events)
                    : ($$"""{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{ {{anthropicInput}},"output_tokens":12} }""", json);
            case "gemini":
                const string gemini = """
                    {"candidates":[{"index":0,"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":100,"cachedContentTokenCount":60,"candidatesTokenCount":10,"thoughtsTokenCount":2}}
                    """;
                return stream ? (Sse(gemini), events) : (gemini, json);
            case "ollama":
                const string ollama = """
                    {"message":{"role":"assistant","content":"ok"},"done":true,"done_reason":"stop","prompt_eval_count":100,"prompt_eval_cached_count":0,"eval_count":12}
                    """;
                return stream ? (ollama + "\n", "application/x-ndjson") : (ollama, json);
            case "responses":
                const string response = """
                    {"status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":100,"output_tokens":12,"output_tokens_details":{"reasoning_tokens":2}}}
                    """;
                const string cacheUsage = """
                    "input_tokens_details":{"cached_tokens":60,"cache_write_tokens":25,"cache_write_tokens_details":{"ttl_30m_tokens":0,"ttl_12h_tokens":25}}
                    """;
                return stream
                    ? (Sse(
                        $$"""{"type":"response.in_progress","response":{"usage":{ {{cacheUsage}} } } }""",
                        $$"""{"type":"response.completed","response":{{response}} }"""), events)
                    : (response.Replace("\"input_tokens\":100", $"{cacheUsage},\"input_tokens\":100", StringComparison.Ordinal), json);
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol));
        }
    }

    private static string Sse(params string[] frames) =>
        string.Concat(frames.Select(frame => $"data: {frame}\n\n"));

    private sealed class UsageHttpHandler : DelegatingHandler
    {
    }

    private sealed class UsageResponseHandler(string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });
    }
}
