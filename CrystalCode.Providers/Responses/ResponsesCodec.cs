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

    // Summary parts and raw reasoning text use separate provider indexes.
    // Crystal segment indexes are one sequence, so traces stay after summaries.
    private const int ReasoningTraceSegmentOffset = 1_000_000;
    private readonly string _vendorName;

    public ResponsesCodec(string vendorName) => _vendorName = vendorName;

    public string Path => "responses";

    public void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public byte[] WriteRequest(ProtocolOptions options, ChatRequest request, bool stream)
    {
        JsonOutputGuard.Reject(request.JsonOutput, _vendorName);
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
        if (root.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String
            && status.GetString() == "failed")
        {
            throw ReadFailure(root);
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            throw CreateException($"{_vendorName} response is missing output items.");
        }

        var items = new List<ChatItem>();
        var refused = false;
        foreach (var item in output.EnumerateArray())
        {
            ReadOutputItem(item, items, ref refused);
        }

        var finish = ReadFinishReason(root, items, refused);
        if (finish != FinishReason.ToolCalls)
        {
            items.RemoveAll(static item => item is ToolCall);
        }

        return new ChatResponse([new ChatCandidate(items, finish)], ReadUsage(root));
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
        if (!ReasoningReplay.IsReplayable(content, ReasoningStateFormat))
        {
            var text = ReasoningReplay.ReadableText(content);
            if (text.Length == 0)
            {
                return;
            }

            WriteReadableReasoningMessage(writer, text);
            return;
        }

        using var document = JsonDocument.Parse(content.State!.Data);
        document.RootElement.WriteTo(writer);
    }

    internal static void WriteReadableReasoningMessage(Utf8JsonWriter writer, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteString("role", ChatRole.Assistant.Value);
        writer.WriteString("content", text);
        writer.WriteEndObject();
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

    private void ReadOutputItem(JsonElement item, List<ChatItem> items, ref bool refused)
    {
        var type = item.GetProperty("type").GetString();
        switch (type)
        {
            case "reasoning":
                var texts = new List<ReasoningText>();
                AppendReasoningTexts(item, "summary", ReasoningTextKind.Summary, texts);
                AppendReasoningTexts(item, "content", ReasoningTextKind.Trace, texts);
                items.Add(new ChatReasoningItem(new ReasoningContent(texts, CreateState(item))));
                break;
            case "message":
                ReadMessage(item, items, ref refused);
                break;
            case "function_call":
                items.Add(new ToolCall(
                    item.GetProperty("call_id").GetString()!,
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("arguments").GetString() ?? ""));
                break;
        }
    }

    private static void ReadMessage(JsonElement item, List<ChatItem> items, ref bool refused)
    {
        if (!item.TryGetProperty("content", out var content))
        {
            return;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            items.Add(new ChatMessage(ChatRole.Assistant, content.GetString() ?? string.Empty));
            return;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var part in content.EnumerateArray())
        {
            var partType = part.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (partType == "output_text"
                && part.TryGetProperty("text", out var text))
            {
                items.Add(new ChatMessage(ChatRole.Assistant, text.GetString() ?? string.Empty));
            }
            else if (partType == "refusal")
            {
                refused = true;
                var refusal = part.TryGetProperty("refusal", out var refusalText)
                    ? refusalText.GetString()
                    : null;
                if (!string.IsNullOrEmpty(refusal))
                {
                    items.Add(new ChatMessage(ChatRole.Assistant, refusal));
                }
            }
        }
    }

    private static void AppendReasoningTexts(
        JsonElement item,
        string propertyName,
        ReasoningTextKind kind,
        List<ReasoningText> texts)
    {
        if (!item.TryGetProperty(propertyName, out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var part in parts.EnumerateArray())
        {
            string? text = null;
            if (part.ValueKind == JsonValueKind.String)
            {
                text = part.GetString();
            }
            else if (part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                text = textElement.GetString();
            }

            if (!string.IsNullOrEmpty(text))
            {
                texts.Add(new ReasoningText(text, kind));
            }
        }
    }

    private static OpaqueReasoningState CreateState(JsonElement item) =>
        new(ReasoningStateFormat, Encoding.UTF8.GetBytes(item.GetRawText()));

    private static FinishReason ReadFinishReason(
        JsonElement root,
        IReadOnlyList<ChatItem> items,
        bool refused)
    {
        if (refused)
        {
            return FinishReason.ContentFilter;
        }

        if (root.TryGetProperty("status", out var status) && status.GetString() == "incomplete")
        {
            return ReadIncomplete(root);
        }

        if (items.Any(static item => item is ToolCall))
        {
            return FinishReason.ToolCalls;
        }

        return FinishReason.Stop;
    }

    private static FinishReason ReadIncomplete(JsonElement response)
    {
        string? reason = null;
        if (response.TryGetProperty("incomplete_details", out var details)
            && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("reason", out var reasonElement)
            && reasonElement.ValueKind == JsonValueKind.String)
        {
            reason = reasonElement.GetString();
        }

        return reason switch
        {
            "content_filter" => FinishReason.ContentFilter,
            "max_output_tokens" => FinishReason.Length,
            null or "" => new FinishReason("incomplete"),
            _ => new FinishReason(reason!)
        };
    }

    private Exception ReadFailure(JsonElement root)
    {
        var message = $"{_vendorName} generation failed.";
        string? code = null;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(messageElement.GetString()))
            {
                message = messageElement.GetString()!;
            }

            if (error.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String)
            {
                code = codeElement.GetString();
            }
        }

        return CreateException(message, errorCode: code);
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
        private bool _refused;
        private bool _refusalTextEmitted;

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
                    events.Add(new ChatReasoningTextDelta(
                        0,
                        outputIndex,
                        ReadIndex(root, "summary_index"),
                        ReasoningTextKind.Summary,
                        root.GetProperty("delta").GetString() ?? ""));
                    break;
                case "response.reasoning_text.delta":
                    events.Add(new ChatReasoningTextDelta(
                        0,
                        outputIndex,
                        checked(ReadIndex(root, "content_index") + ReasoningTraceSegmentOffset),
                        ReasoningTextKind.Trace,
                        root.GetProperty("delta").GetString() ?? ""));
                    break;
                case "response.output_text.delta":
                    events.Add(new ChatTextDelta(0, outputIndex, ChatRole.Assistant, root.GetProperty("delta").GetString() ?? ""));
                    break;
                case "response.refusal.delta":
                    _refused = true;
                    _refusalTextEmitted = true;
                    events.Add(new ChatTextDelta(
                        0,
                        outputIndex,
                        ChatRole.Assistant,
                        root.GetProperty("delta").GetString() ?? ""));
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

                    var finish = type == "response.incomplete"
                        ? ReadIncomplete(response)
                        : _refused
                            ? FinishReason.ContentFilter
                            : _hasTools
                                ? FinishReason.ToolCalls
                                : FinishReason.Stop;
                    events.Add(new ChatCandidateCompleted(0, finish));
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

        private void ReadDoneItem(JsonElement root, int outputIndex, List<ChatStreamEvent> events)
        {
            var item = root.GetProperty("item");
            var type = item.GetProperty("type").GetString();
            if (type == "reasoning")
            {
                events.Add(new ChatReasoningStateReceived(0, outputIndex, CreateState(item)));
                return;
            }

            if (type != "message" || _refusalTextEmitted || !item.TryGetProperty("content", out var content))
            {
                return;
            }

            if (content.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var partType)
                    && partType.GetString() == "refusal"
                    && part.TryGetProperty("refusal", out var refusal)
                    && !string.IsNullOrEmpty(refusal.GetString()))
                {
                    _refused = true;
                    _refusalTextEmitted = true;
                    events.Add(new ChatTextDelta(
                        0,
                        outputIndex,
                        ChatRole.Assistant,
                        refusal.GetString()!));
                }
            }
        }

        private static int ReadIndex(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var index) && index.ValueKind == JsonValueKind.Number)
            {
                return index.GetInt32();
            }

            return 0;
        }
    }
}
