using System.Text.Json;

using System.Text;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Gemini;

using Xunit;

namespace CrystalCode.Providers.Tests.Gemini;

public sealed class GeminiProviderTests
{
    [Fact]
    public async Task CompleteAsync_ReplaysSignedToolCallAndResult()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """
            {"candidates":[{"content":{"parts":[
              {"functionCall":{"id":"call_1","name":"read","args":{"path":"a"}},"thoughtSignature":"signed"}
            ]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":3,"thoughtsTokenCount":2}}
            """));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new GeminiProvider(
            new GeminiOptions("test-key", "gemini-test"), firstHttp);
        using var schema = JsonDocument.Parse("{\"type\":\"object\"}");
        var request = new ChatRequest(
            [new ChatMessage(ChatRole.System, "system"), new ChatMessage(ChatRole.User, "read a")],
            [new ToolDefinition("read", schema.RootElement, "Read a file")]);

        var first = await firstProvider.CompleteAsync(request);

        Assert.Equal(new Uri("https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent"),
            firstHandler.Request!.RequestUri);
        Assert.Equal("test-key", firstHandler.Request.Headers.GetValues("x-goog-api-key").Single());
        Assert.Null(firstHandler.Request.Headers.Authorization);
        Assert.Equal(FinishReason.ToolCalls, first.Candidates[0].FinishReason);
        Assert.Equal(5, first.Usage!.OutputTokenCount);
        var call = Assert.IsType<ToolCall>(first.Candidates[0].Items[0]);

        var secondHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var secondHttp = new HttpClient(secondHandler);
        using var secondProvider = new GeminiProvider(
            new GeminiOptions("test-key", "gemini-test"), secondHttp);
        await secondProvider.CompleteAsync(new ChatRequest(
            [.. request.Items, .. first.Candidates[0].Items, new ToolResult(call.CallId, "contents")],
            request.Tools));

        using var sent = JsonDocument.Parse(secondHandler.Body!);
        var contents = sent.RootElement.GetProperty("contents");
        Assert.Equal("signed", contents[1].GetProperty("parts")[0]
            .GetProperty("thoughtSignature").GetString());
        Assert.Equal("call_1", contents[2].GetProperty("parts")[0]
            .GetProperty("functionResponse").GetProperty("id").GetString());
    }

    [Fact]
    public async Task StreamAsync_EmitsToolSignatureAndCompletion()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"candidates":[{"index":0,"content":{"parts":[{"functionCall":{"id":"call_1","name":"read","args":{}},"thoughtSignature":"signed"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":3,"candidatesTokenCount":1}}

            """));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("test-key", "gemini-test"), http);
        var events = new List<ChatStreamEvent>();

        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Contains(events, item => item is ChatToolCallDelta { CallIdDelta: "call_1" });
        Assert.Contains(events, item => item is ChatReasoningStateReceived);
        Assert.Contains(events, item => item is ChatCandidateCompleted);
    }

    [Fact]
    public async Task StreamAsync_IgnoresNullFinishReasonUntilTheCandidateStops()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"candidates":[{"index":0,"content":{"parts":[{"text":"Hello"}]},"finishReason":null}]}

            data: {"candidates":[{"index":0,"content":{"parts":[{"text":" world"}]},"finishReason":"STOP"}]}

            """));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("test-key", "gemini-test"), http);
        var events = new List<ChatStreamEvent>();

        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Equal(2, events.OfType<ChatTextDelta>().Count());
        Assert.Equal(
            "Hello world",
            string.Concat(events.OfType<ChatTextDelta>().Select(static delta => delta.Text)));
        Assert.Single(events.OfType<ChatCandidateCompleted>());
    }

    [Fact]
    public async Task CompleteAsync_ReplaysNativeCallWithoutInventingWireId()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"read","args":{}},"thoughtSignature":"signed"}]},"finishReason":"STOP"}]}"""));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), firstHttp);
        var original = new ChatRequest([new ChatMessage(ChatRole.User, "read")]);
        var response = await firstProvider.CompleteAsync(original);
        var call = Assert.IsType<ToolCall>(response.Candidates[0].Items[0]);

        var nextHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var nextHttp = new HttpClient(nextHandler);
        using var nextProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), nextHttp);
        await nextProvider.CompleteAsync(new ChatRequest(
            [.. original.Items, .. response.Candidates[0].Items, new ToolResult(call.CallId, "ok")]));

        using var sent = JsonDocument.Parse(nextHandler.Body!);
        var contents = sent.RootElement.GetProperty("contents");
        var replayCall = contents[1].GetProperty("parts")[0].GetProperty("functionCall");
        var replayResult = contents[2].GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.False(replayCall.TryGetProperty("id", out _));
        Assert.False(replayResult.TryGetProperty("id", out _));
        Assert.Equal("signed", contents[1].GetProperty("parts")[0]
            .GetProperty("thoughtSignature").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ReplaysSignatureOnlyPart()
    {
        var firstHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"answer"},{"text":"","thoughtSignature":"signed"}]},"finishReason":"STOP"}]}"""));
        using var firstHttp = new HttpClient(firstHandler);
        using var firstProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), firstHttp);
        var original = new ChatRequest([new ChatMessage(ChatRole.User, "question")]);
        var response = await firstProvider.CompleteAsync(original);

        var nextHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"next"}]},"finishReason":"STOP"}]}"""));
        using var nextHttp = new HttpClient(nextHandler);
        using var nextProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-test"), nextHttp);
        await nextProvider.CompleteAsync(new ChatRequest(
            [.. original.Items, .. response.Candidates[0].Items, new ChatMessage(ChatRole.User, "next")]));

        using var sent = JsonDocument.Parse(nextHandler.Body!);
        var parts = sent.RootElement.GetProperty("contents")[1].GetProperty("parts");
        Assert.Equal("signed", parts[1].GetProperty("thoughtSignature").GetString());
        Assert.Equal(string.Empty, parts[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task StreamAsync_ReplaysAccumulatedTextWithSignature()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"candidates":[{"index":0,"content":{"parts":[{"text":"Hel"}]}}]}

            data: {"candidates":[{"index":0,"content":{"parts":[{"text":"lo","thoughtSignature":"sig"}]},"finishReason":"STOP"}]}

            """));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), http);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        var state = Assert.Single(events.OfType<ChatReasoningStateReceived>()).State;
        using var signed = JsonDocument.Parse(state.Data);
        Assert.Equal("Hello", signed.RootElement.GetProperty("text").GetString());
        Assert.Equal("sig", signed.RootElement.GetProperty("thoughtSignature").GetString());

        var replay = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"next"}]},"finishReason":"STOP"}]}"""));
        using var replayHttp = new HttpClient(replay);
        using var replayProvider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), replayHttp);
        await replayProvider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "hi"),
            new ChatMessage(ChatRole.Assistant, "Hello"),
            new ChatReasoningItem(new ReasoningContent(state: state))
        ]));

        using var sent = JsonDocument.Parse(replay.Body!);
        var parts = sent.RootElement.GetProperty("contents")[1].GetProperty("parts");
        Assert.Equal(1, parts.GetArrayLength());
        Assert.Equal("Hello", parts[0].GetProperty("text").GetString());
        Assert.Equal("sig", parts[0].GetProperty("thoughtSignature").GetString());
    }

    [Fact]
    public async Task StreamAsync_KeepsEmptySignedPartSeparate()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"candidates":[{"index":0,"content":{"parts":[{"text":"answer"}]}}]}

            data: {"candidates":[{"index":0,"content":{"parts":[{"text":"","thoughtSignature":"signed"}]},"finishReason":"STOP"}]}

            """));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), http);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "question")])))
        {
            events.Add(item);
        }

        var state = Assert.Single(events.OfType<ChatReasoningStateReceived>()).State;
        Assert.Contains("\"text\":\"\"", Encoding.UTF8.GetString(state.Data.Span), StringComparison.Ordinal);

        var replay = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"next"}]},"finishReason":"STOP"}]}"""));
        using var replayHttp = new HttpClient(replay);
        using var replayProvider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), replayHttp);
        await replayProvider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "question"),
            new ChatMessage(ChatRole.Assistant, "answer"),
            new ChatReasoningItem(new ReasoningContent(state: state))
        ]));

        using var sent = JsonDocument.Parse(replay.Body!);
        var parts = sent.RootElement.GetProperty("contents")[1].GetProperty("parts");
        Assert.Equal(2, parts.GetArrayLength());
        Assert.Equal("answer", parts[0].GetProperty("text").GetString());
        Assert.Equal(string.Empty, parts[1].GetProperty("text").GetString());
        Assert.Equal("signed", parts[1].GetProperty("thoughtSignature").GetString());
    }

    [Fact]
    public async Task CompleteAsync_PreservesMalformedFunctionCallWithoutTheCall()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """
            {"candidates":[{"content":{"parts":[
              {"text":"no"},
              {"functionCall":{"name":"read","args":{}}}
            ]},"finishReason":"MALFORMED_FUNCTION_CALL"}]}
            """));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), http);

        var response = await provider.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "read")]));

        Assert.Equal("MALFORMED_FUNCTION_CALL", response.Candidates[0].FinishReason.Value);
        Assert.Equal("no", Assert.IsType<ChatMessage>(Assert.Single(response.Candidates[0].Items)).Text);
    }

    [Fact]
    public async Task CompleteAsync_MapsSafetyAndDropsToolsOnMaxTokens()
    {
        var safetyHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"blocked"}]},"finishReason":"SAFETY"}]}"""));
        using var safetyHttp = new HttpClient(safetyHandler);
        using var safety = new GeminiProvider(new GeminiOptions("key", "gemini-test"), safetyHttp);
        var filtered = await safety.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")]));
        Assert.Equal(FinishReason.ContentFilter, filtered.Candidates[0].FinishReason);
        Assert.Equal("blocked", Assert.IsType<ChatMessage>(filtered.Candidates[0].Items[0]).Text);

        var lengthHandler = new RecordingHandler(JsonResponse.Create(
            """
            {"candidates":[{"content":{"parts":[
              {"text":"cut"},
              {"functionCall":{"name":"read","args":{}}}
            ]},"finishReason":"MAX_TOKENS"}]}
            """));
        using var lengthHttp = new HttpClient(lengthHandler);
        using var length = new GeminiProvider(new GeminiOptions("key", "gemini-test"), lengthHttp);
        var truncated = await length.CompleteAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")]));
        Assert.Equal(FinishReason.Length, truncated.Candidates[0].FinishReason);
        Assert.Equal("cut", Assert.IsType<ChatMessage>(Assert.Single(truncated.Candidates[0].Items)).Text);
    }

    [Fact]
    public async Task CompleteAsync_RejectsDisablingGemini3Thinking()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-3.8-flash"), http);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "hi")],
                reasoning: new ReasoningOptions(ReasoningMode.Disabled))));

        Assert.Equal("Gemini 3 thinking cannot be disabled.", exception.Message);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task CompleteAsync_RejectsMaximumEffortAndNonGemini3Effort()
    {
        var gemini3 = new RecordingHandler(JsonResponse.Create("{}"));
        using var gemini3Http = new HttpClient(gemini3);
        using var gemini3Provider = new GeminiProvider(
            new GeminiOptions("key", "gemini-3.8-flash"),
            gemini3Http);
        var maximum = await Assert.ThrowsAsync<NotSupportedException>(
            () => gemini3Provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "hi")],
                reasoning: new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.Maximum))));
        Assert.Contains("maximum", maximum.Message, StringComparison.Ordinal);

        var older = new RecordingHandler(JsonResponse.Create("{}"));
        using var olderHttp = new HttpClient(older);
        using var olderProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-2.0-flash"),
            olderHttp);
        var effort = await Assert.ThrowsAsync<NotSupportedException>(
            () => olderProvider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "hi")],
                reasoning: new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.High))));
        Assert.Contains("token budget", effort.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_MapsGemini25EffortToThinkingBudget()
    {
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(
            new GeminiOptions("key", "gemini-2.5-flash"),
            http);

        await provider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Enabled, ReasoningEffort.High)));

        Assert.Contains("\"thinkingBudget\":24576", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("thinkingLevel", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_DropsSignedFunctionCallsThatAreNotToolRequests()
    {
        await AssertDroppedSignedCall("MAX_TOKENS", FinishReason.Length);
        await AssertDroppedSignedCall("MALFORMED_FUNCTION_CALL", new FinishReason("MALFORMED_FUNCTION_CALL"));
    }

    [Fact]
    public async Task StreamAsync_EmitsHeldToolCallWhenFinishArrivesLater()
    {
        var handler = new RecordingHandler(JsonResponse.CreateStream(
            """
            data: {"candidates":[{"index":0,"content":{"parts":[{"functionCall":{"id":"call_1","name":"read","args":{}},"thoughtSignature":"signed"}]}}]}

            data: {"candidates":[{"index":0,"finishReason":"STOP"}]}

            """));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), http);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.Contains(events, item => item is ChatToolCallDelta { CallIdDelta: "call_1" });
        Assert.Contains(events, item => item is ChatReasoningStateReceived);
        var completed = Assert.Single(events.OfType<ChatCandidateCompleted>());
        Assert.Equal(FinishReason.ToolCalls, completed.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_SkipsFunctionCallSignatureWithoutAToolCall()
    {
        var state = new OpaqueReasoningState(
            GeminiCodec.PartStateFormat,
            Encoding.UTF8.GetBytes(
                """{"functionCall":{"name":"read","args":{}},"thoughtSignature":"sig"}"""));
        var handler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"next"}]},"finishReason":"STOP"}]}"""));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), http);

        await provider.CompleteAsync(new ChatRequest(
        [
            new ChatMessage(ChatRole.User, "hi"),
            new ChatMessage(ChatRole.Assistant, "cut"),
            new ChatReasoningItem(new ReasoningContent(state: state)),
            new ChatMessage(ChatRole.User, "next")
        ]));

        using var sent = JsonDocument.Parse(handler.Body!);
        var parts = sent.RootElement.GetProperty("contents")[1].GetProperty("parts");
        Assert.Equal(1, parts.GetArrayLength());
        Assert.Equal("cut", parts[0].GetProperty("text").GetString());
        Assert.DoesNotContain("functionCall", handler.Body, StringComparison.Ordinal);
    }

    private static async Task AssertDroppedSignedCall(string wireReason, FinishReason expected)
    {
        var payload = """
            data: {"candidates":[{"index":0,"content":{"parts":[{"text":"cut"}]}}]}

            data: {"candidates":[{"index":0,"content":{"parts":[{"functionCall":{"name":"read","args":{}},"thoughtSignature":"sig"}]}}]}

            data: {"candidates":[{"index":0,"finishReason":"WIRE_REASON"}]}

            """.Replace("WIRE_REASON", wireReason, StringComparison.Ordinal);
        var handler = new RecordingHandler(JsonResponse.CreateStream(payload));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), http);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in provider.StreamAsync(
            new ChatRequest([new ChatMessage(ChatRole.User, "hi")])))
        {
            events.Add(item);
        }

        Assert.DoesNotContain(events, static item => item is ChatToolCallDelta);
        Assert.DoesNotContain(events, static item => item is ChatReasoningStateReceived);
        Assert.Equal("cut", string.Concat(events.OfType<ChatTextDelta>().Select(static delta => delta.Text)));
        Assert.Equal(expected, Assert.Single(events.OfType<ChatCandidateCompleted>()).FinishReason);

        var replay = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"next"}]},"finishReason":"STOP"}]}"""));
        using var replayHttp = new HttpClient(replay);
        using var replayProvider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), replayHttp);
        var items = new List<ChatItem>
        {
            new ChatMessage(ChatRole.User, "hi"),
            new ChatMessage(ChatRole.Assistant, "cut")
        };
        items.AddRange(events.OfType<ChatReasoningStateReceived>().Select(static state =>
            new ChatReasoningItem(new ReasoningContent(state: state.State))));
        items.Add(new ChatMessage(ChatRole.User, "next"));
        await replayProvider.CompleteAsync(new ChatRequest(items));
        Assert.DoesNotContain("functionCall", replay.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_WritesThinkingBudgetForOlderGeminiModels()
    {
        var budgetHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var budgetHttp = new HttpClient(budgetHandler);
        using var budgetProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-2.5-flash"),
            budgetHttp);
        await budgetProvider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "hi")],
            reasoning: new ReasoningOptions(tokenBudget: 2048)));
        Assert.Contains("\"thinkingBudget\":2048", budgetHandler.Body, StringComparison.Ordinal);

        var disabledHandler = new RecordingHandler(JsonResponse.Create(
            """{"candidates":[{"content":{"parts":[{"text":"done"}]},"finishReason":"STOP"}]}"""));
        using var disabledHttp = new HttpClient(disabledHandler);
        using var disabledProvider = new GeminiProvider(
            new GeminiOptions("key", "gemini-2.5-flash"),
            disabledHttp);
        await disabledProvider.CompleteAsync(new ChatRequest(
            [new ChatMessage(ChatRole.User, "hi")],
            reasoning: new ReasoningOptions(ReasoningMode.Disabled)));
        Assert.Contains("\"thinkingBudget\":0", disabledHandler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_RejectsUnmatchedToolCallAndJsonOutput()
    {
        var handler = new RecordingHandler(JsonResponse.Create("{}"));
        using var http = new HttpClient(handler);
        using var provider = new GeminiProvider(new GeminiOptions("key", "gemini-test"), http);

        var missing = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
            [
                new ChatMessage(ChatRole.User, "read"),
                new ToolCall("call_1", "read", "{}")
            ])));
        Assert.Contains("missing a tool result for 'call_1'", missing.Message, StringComparison.Ordinal);

        var json = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CompleteAsync(new ChatRequest(
                [new ChatMessage(ChatRole.User, "json")],
                jsonOutput: JsonResponse.OutputSchema())));
        Assert.Contains("does not support a JSON output schema", json.Message, StringComparison.Ordinal);
        Assert.Null(handler.Body);
    }
}
