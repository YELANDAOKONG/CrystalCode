using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Responses;

internal sealed class ResponsesCodec : IProtocolCodec
{
    internal const string ReasoningStateFormat = "openai.responses.reasoning";
    private readonly string _vendorName;

    public ResponsesCodec(string vendorName) => _vendorName = vendorName;

    public string Path => "responses";

    public void AddHeaders(HttpRequestMessage request, string apiKey) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    public byte[] WriteRequest(ProtocolOptions options, ChatRequest request, bool stream)
    {
        if (request.Items.Count == 0)
        {
            throw new ArgumentException($"{_vendorName} requires at least one transcript item.", nameof(request));
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", options.Model);
            writer.WritePropertyName("input");
            writer.WriteStartArray();
            foreach (var item in request.Items)
            {
                WriteItem(writer, item);
            }

            writer.WriteEndArray();
            WriteTools(writer, request.Tools);
            WriteReasoning(writer, request.Reasoning);
            if (options.MaxTokens is { } maxTokens)
            {
                writer.WriteNumber("max_output_tokens", maxTokens);
            }

            if (options.Temperature is { } temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            if (options.TopP is { } topP)
            {
                writer.WriteNumber("top_p", topP);
            }

            writer.WriteBoolean("store", false);
            writer.WritePropertyName("include");
            writer.WriteStartArray();
            writer.WriteStringValue("reasoning.encrypted_content");
            writer.WriteEndArray();
            writer.WriteBoolean("stream", stream);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public ChatResponse ReadResponse(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            throw CreateException($"{_vendorName} response is missing output items.");
        }

        var items = new List<ChatItem>();
        foreach (var item in output.EnumerateArray())
        {
            ReadOutputItem(item, items);
        }

        return new ChatResponse(
            [new ChatCandidate(items, ReadFinishReason(root, items))],
            ReadUsage(root));
    }

    public IProtocolStreamParser CreateStreamParser() => new StreamParser(this);

    public Exception CreateException(string message, int? statusCode = null, Exception? innerException = null, string? errorCode = null, TimeSpan? retryAfter = null) =>
        new ResponsesException(message, statusCode, innerException, errorCode, retryAfter);

    private static void WriteItem(Utf8JsonWriter writer, ChatItem item)
    {
        switch (item)
        {
            case ChatMessage message:
                writer.WriteStartObject();
                writer.WriteString("type", "message");
                writer.WriteString("role", message.Role.Value);
                writer.WriteString("content", message.Text);
                writer.WriteEndObject();
                break;
            case ChatReasoningItem reasoning:
                WriteReasoningItem(writer, reasoning.Content);
                break;
            case ToolCall toolCall:
                writer.WriteStartObject();
                writer.WriteString("type", "function_call");
                writer.WriteString("call_id", toolCall.CallId);
                writer.WriteString("name", toolCall.Name);
                writer.WriteString("arguments", toolCall.Arguments);
                writer.WriteEndObject();
                break;
            case ToolResult toolResult:
                writer.WriteStartObject();
                writer.WriteString("type", "function_call_output");
                writer.WriteString("call_id", toolResult.CallId);
                writer.WriteString("output", toolResult.Text);
                writer.WriteEndObject();
                break;
            default:
                throw new NotSupportedException($"Responses does not support chat item type {item.GetType().Name}.");
        }
    }

    private static void WriteReasoningItem(Utf8JsonWriter writer, ReasoningContent content)
    {
        if (content.State is null || content.State.Format != ReasoningStateFormat)
        {
            throw new NotSupportedException("Responses requires its opaque reasoning item for replay.");
        }

        using var document = JsonDocument.Parse(content.State.Data);
        document.RootElement.WriteTo(writer);
    }

    private static void WriteTools(Utf8JsonWriter writer, IReadOnlyList<ToolDefinition> tools)
    {
        if (tools.Count == 0)
        {
            return;
        }

        writer.WritePropertyName("tools");
        writer.WriteStartArray();
        foreach (var tool in tools)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WriteString("name", tool.Name);
            if (tool.Description is not null)
            {
                writer.WriteString("description", tool.Description);
            }

            writer.WritePropertyName("parameters");
            tool.InputSchema.WriteTo(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteReasoning(Utf8JsonWriter writer, ReasoningOptions? reasoning)
    {
        if (reasoning is null)
        {
            return;
        }

        if (reasoning.TokenBudget is not null)
        {
            throw new NotSupportedException("Responses does not support a reasoning token budget.");
        }

        ValidateReasoning(reasoning);
        var disabled = reasoning.Mode == ReasoningMode.Disabled
            || reasoning.Output == ReasoningOutput.None;

        writer.WritePropertyName("reasoning");
        writer.WriteStartObject();
        if (disabled)
        {
            writer.WriteString("effort", "none");
        }
        else if (reasoning.Effort is { } effort)
        {
            writer.WriteString("effort", effort == ReasoningEffort.Maximum ? "xhigh" : effort.Value);
        }

        if (!disabled)
        {
            writer.WriteString("summary", "auto");
        }

        writer.WriteEndObject();
    }

    private static void ValidateReasoning(ReasoningOptions reasoning)
    {
        if (reasoning.Mode is { } mode
            && mode != ReasoningMode.Automatic
            && mode != ReasoningMode.Enabled
            && mode != ReasoningMode.Disabled)
        {
            throw new NotSupportedException($"Responses does not support reasoning mode '{mode.Value}'.");
        }

        if (reasoning.Output is { } output
            && output != ReasoningOutput.None
            && output != ReasoningOutput.Summary)
        {
            throw new NotSupportedException($"Responses does not support reasoning output '{output.Value}'.");
        }

        if (reasoning.Mode == ReasoningMode.Disabled && reasoning.Output == ReasoningOutput.Summary)
        {
            throw new NotSupportedException(
                "Responses cannot disable reasoning while requesting a reasoning summary.");
        }

        if (reasoning.Mode == ReasoningMode.Enabled && reasoning.Output == ReasoningOutput.None)
        {
            throw new NotSupportedException(
                "Responses cannot enable reasoning while requesting no reasoning output.");
        }

        if ((reasoning.Mode == ReasoningMode.Disabled || reasoning.Output == ReasoningOutput.None)
            && reasoning.Effort is not null)
        {
            throw new NotSupportedException(
                "Responses does not apply reasoning effort when reasoning is disabled.");
        }
    }

    private void ReadOutputItem(JsonElement item, List<ChatItem> items)
    {
        var type = item.GetProperty("type").GetString();
        switch (type)
        {
            case "reasoning":
                var texts = new List<ReasoningText>();
                if (item.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in summary.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var text))
                        {
                            texts.Add(new ReasoningText(text.GetString() ?? "", ReasoningTextKind.Summary));
                        }
                    }
                }

                items.Add(new ChatReasoningItem(new ReasoningContent(texts, CreateState(item))));
                break;
            case "message":
                if (item.TryGetProperty("content", out var content))
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var partType)
                            && partType.GetString() == "output_text"
                            && part.TryGetProperty("text", out var text))
                        {
                            items.Add(new ChatMessage(ChatRole.Assistant, text.GetString() ?? ""));
                        }
                    }
                }

                break;
            case "function_call":
                items.Add(new ToolCall(
                    item.GetProperty("call_id").GetString()!,
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("arguments").GetString() ?? ""));
                break;
        }
    }

    private static OpaqueReasoningState CreateState(JsonElement item) =>
        new(ReasoningStateFormat, Encoding.UTF8.GetBytes(item.GetRawText()));

    private static FinishReason ReadFinishReason(JsonElement root, IReadOnlyList<ChatItem> items)
    {
        if (items.Any(static item => item is ToolCall))
        {
            return FinishReason.ToolCalls;
        }

        if (root.TryGetProperty("status", out var status) && status.GetString() == "incomplete")
        {
            return FinishReason.Length;
        }

        return FinishReason.Stop;
    }

    private static TokenUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var input = usage.GetProperty("input_tokens").GetInt64();
        var output = usage.GetProperty("output_tokens").GetInt64();
        long? reasoning = null;
        if (usage.TryGetProperty("output_tokens_details", out var details)
            && details.TryGetProperty("reasoning_tokens", out var reasoningElement))
        {
            reasoning = reasoningElement.GetInt64();
        }

        return new TokenUsage(input, output, reasoning);
    }

    private sealed class StreamParser : IProtocolStreamParser
    {
        private readonly ResponsesCodec _codec;
        private bool _hasTools;

        public StreamParser(ResponsesCodec codec) => _codec = codec;

        public bool IsComplete { get; private set; }

        public IReadOnlyList<ChatStreamEvent> Parse(JsonElement root)
        {
            var events = new List<ChatStreamEvent>();
            if (!root.TryGetProperty("type", out var typeElement))
            {
                return events;
            }

            var type = typeElement.GetString();
            var outputIndex = root.TryGetProperty("output_index", out var indexElement)
                ? indexElement.GetInt32()
                : 0;
            switch (type)
            {
                case "response.reasoning_summary_text.delta":
                case "response.reasoning_text.delta":
                    events.Add(new ChatReasoningTextDelta(0, outputIndex, 0, ReasoningTextKind.Summary, root.GetProperty("delta").GetString() ?? ""));
                    break;
                case "response.output_text.delta":
                    events.Add(new ChatTextDelta(0, outputIndex, ChatRole.Assistant, root.GetProperty("delta").GetString() ?? ""));
                    break;
                case "response.output_item.added":
                    ReadAddedItem(root, outputIndex, events);
                    break;
                case "response.function_call_arguments.delta":
                    events.Add(new ChatToolCallDelta(0, outputIndex, "", "", root.GetProperty("delta").GetString() ?? ""));
                    break;
                case "response.output_item.done":
                    ReadDoneItem(root, outputIndex, events);
                    break;
                case "response.completed":
                case "response.incomplete":
                    var response = root.GetProperty("response");
                    var usage = ReadUsage(response);
                    if (usage is not null)
                    {
                        events.Add(new ChatUsageReceived(usage));
                    }

                    events.Add(new ChatCandidateCompleted(0, _hasTools ? FinishReason.ToolCalls : type == "response.incomplete" ? FinishReason.Length : FinishReason.Stop));
                    IsComplete = true;
                    break;
                case "response.failed":
                    var failedResponse = root.GetProperty("response");
                    var failure = failedResponse.GetProperty("error");
                    throw _codec.CreateException(
                        failure.GetProperty("message").GetString() ?? "Responses stream failed.",
                        errorCode: failure.TryGetProperty("code", out var failureCode)
                            ? failureCode.GetString()
                            : null);
                case "error":
                    var error = root.GetProperty("error");
                    throw _codec.CreateException(error.GetProperty("message").GetString() ?? "Responses stream failed.", errorCode: error.TryGetProperty("code", out var code) ? code.GetString() : null);
            }

            return events;
        }

        private void ReadAddedItem(JsonElement root, int outputIndex, List<ChatStreamEvent> events)
        {
            var item = root.GetProperty("item");
            var type = item.GetProperty("type").GetString();
            if (type == "function_call")
            {
                var callId = item.GetProperty("call_id").GetString() ?? "";
                var name = item.GetProperty("name").GetString() ?? "";
                _hasTools = true;
                events.Add(new ChatToolCallDelta(0, outputIndex, callId, name, ""));
            }
        }

        private static void ReadDoneItem(JsonElement root, int outputIndex, List<ChatStreamEvent> events)
        {
            var item = root.GetProperty("item");
            if (item.GetProperty("type").GetString() == "reasoning")
            {
                events.Add(new ChatReasoningStateReceived(0, outputIndex, CreateState(item)));
            }
        }
    }
}
