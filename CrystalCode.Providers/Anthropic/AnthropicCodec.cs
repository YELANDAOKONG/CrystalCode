using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Anthropic;

internal sealed class AnthropicCodec : IProtocolCodec
{
    internal const string ReasoningStateFormat = "anthropic.messages.thinking";
    private const int DefaultMaxTokens = 8192;
    private const string AnthropicVersion = "2023-06-01";
    private readonly string _vendorName;

    public AnthropicCodec(string vendorName) => _vendorName = vendorName;

    public string Path => "messages";

    public void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
    }

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
            WriteSystem(writer, request.Items);
            writer.WriteNumber("max_tokens", options.MaxTokens ?? DefaultMaxTokens);
            WriteMessages(writer, request.Items);
            WriteTools(writer, request.Tools);
            WriteReasoning(writer, request.Reasoning);
            if (options.Temperature is { } temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            if (options.TopP is { } topP)
            {
                writer.WriteNumber("top_p", topP);
            }

            writer.WriteBoolean("stream", stream);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public ChatResponse ReadResponse(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            throw CreateException($"{_vendorName} response is missing content blocks.");
        }

        var items = new List<ChatItem>();
        foreach (var block in content.EnumerateArray())
        {
            ReadBlock(block, items);
        }

        var stopReason = root.TryGetProperty("stop_reason", out var stop)
            ? ReadFinishReason(stop.GetString())
            : FinishReason.Stop;
        return new ChatResponse([new ChatCandidate(items, stopReason)], ReadUsage(root));
    }

    public IProtocolStreamParser CreateStreamParser() => new StreamParser(this);

    public Exception CreateException(string message, int? statusCode = null, Exception? innerException = null, string? errorCode = null, TimeSpan? retryAfter = null) =>
        new AnthropicException(message, statusCode, innerException, errorCode, retryAfter);

    private static void WriteSystem(Utf8JsonWriter writer, IReadOnlyList<ChatItem> items)
    {
        var systems = items.OfType<ChatMessage>()
            .Where(static message => message.Role == ChatRole.System)
            .Select(static message => message.Text)
            .ToArray();
        if (systems.Length > 0)
        {
            writer.WriteString("system", string.Join("\n\n", systems));
        }
    }

    private static void WriteMessages(Utf8JsonWriter writer, IReadOnlyList<ChatItem> items)
    {
        writer.WritePropertyName("messages");
        writer.WriteStartArray();
        string? role = null;
        var blocks = new List<ChatItem>();
        void Flush()
        {
            if (role is null || blocks.Count == 0)
            {
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("role", role);
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            foreach (var block in blocks)
            {
                WriteBlock(writer, block);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            blocks.Clear();
        }

        foreach (var item in items)
        {
            var nextRole = item switch
            {
                ChatMessage message when message.Role == ChatRole.System => null,
                ChatMessage message => message.Role == ChatRole.Assistant ? "assistant" : "user",
                ChatReasoningItem => "assistant",
                ToolCall => "assistant",
                ToolResult => "user",
                _ => throw new NotSupportedException($"Anthropic does not support chat item type {item.GetType().Name}.")
            };
            if (nextRole is null)
            {
                continue;
            }

            if (role is not null && role != nextRole)
            {
                Flush();
            }

            role = nextRole;
            blocks.Add(item);
        }

        Flush();
        writer.WriteEndArray();
    }

    private static void WriteBlock(Utf8JsonWriter writer, ChatItem item)
    {
        switch (item)
        {
            case ChatMessage message:
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", message.Text);
                writer.WriteEndObject();
                break;
            case ChatReasoningItem reasoning:
                WriteReasoningBlock(writer, reasoning.Content);
                break;
            case ToolCall call:
                writer.WriteStartObject();
                writer.WriteString("type", "tool_use");
                writer.WriteString("id", call.CallId);
                writer.WriteString("name", call.Name);
                writer.WritePropertyName("input");
                using (var document = JsonDocument.Parse(call.Arguments))
                {
                    document.RootElement.WriteTo(writer);
                }

                writer.WriteEndObject();
                break;
            case ToolResult result:
                writer.WriteStartObject();
                writer.WriteString("type", "tool_result");
                writer.WriteString("tool_use_id", result.CallId);
                writer.WriteString("content", result.Text);
                writer.WriteBoolean("is_error", result.Status != ToolResultStatus.Success);
                writer.WriteEndObject();
                break;
            default:
                throw new NotSupportedException(
                    $"Anthropic does not support chat item type {item.GetType().Name}.");
        }
    }

    private static void WriteReasoningBlock(Utf8JsonWriter writer, ReasoningContent content)
    {
        if (content.State is null || content.State.Format != ReasoningStateFormat)
        {
            throw new NotSupportedException("Anthropic requires its signed opaque thinking block for replay.");
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
            writer.WriteString("name", tool.Name);
            if (tool.Description is not null)
            {
                writer.WriteString("description", tool.Description);
            }

            writer.WritePropertyName("input_schema");
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

        ValidateReasoning(reasoning);

        var disabled = reasoning.Mode == ReasoningMode.Disabled
            || reasoning.Output == ReasoningOutput.None;
        var enabled = reasoning.Mode == ReasoningMode.Enabled
            || reasoning.Output == ReasoningOutput.Summary
            || reasoning.TokenBudget is not null;

        if (disabled || enabled)
        {
            writer.WritePropertyName("thinking");
            writer.WriteStartObject();
            if (disabled)
            {
                writer.WriteString("type", "disabled");
            }
            else if (reasoning.TokenBudget is { } budget)
            {
                writer.WriteString("type", "enabled");
                writer.WriteNumber("budget_tokens", budget);
            }
            else
            {
                writer.WriteString("type", "adaptive");
            }

            if (!disabled && reasoning.Output == ReasoningOutput.Summary)
            {
                writer.WriteString("display", "summarized");
            }

            writer.WriteEndObject();
        }

        if (!disabled && reasoning.Effort is { } effort)
        {
            writer.WritePropertyName("output_config");
            writer.WriteStartObject();
            writer.WriteString("effort", ResolveEffort(effort));
            writer.WriteEndObject();
        }
    }

    private static void ValidateReasoning(ReasoningOptions reasoning)
    {
        if (reasoning.Mode is { } mode
            && mode != ReasoningMode.Automatic
            && mode != ReasoningMode.Enabled
            && mode != ReasoningMode.Disabled)
        {
            throw new NotSupportedException($"Anthropic does not support reasoning mode '{mode.Value}'.");
        }

        if (reasoning.Output is { } output
            && output != ReasoningOutput.None
            && output != ReasoningOutput.Summary)
        {
            throw new NotSupportedException($"Anthropic does not support reasoning output '{output.Value}'.");
        }

        if (reasoning.Mode == ReasoningMode.Disabled && reasoning.Output == ReasoningOutput.Summary)
        {
            throw new NotSupportedException(
                "Anthropic cannot disable thinking while requesting a thinking summary.");
        }

        if (reasoning.Mode == ReasoningMode.Enabled && reasoning.Output == ReasoningOutput.None)
        {
            throw new NotSupportedException(
                "Anthropic cannot enable thinking while requesting no thinking output.");
        }

        if ((reasoning.Mode == ReasoningMode.Disabled || reasoning.Output == ReasoningOutput.None)
            && reasoning.Effort is not null)
        {
            throw new NotSupportedException(
                "Anthropic does not apply reasoning effort when thinking is disabled.");
        }

        if (reasoning.TokenBudget is < 1024)
        {
            throw new NotSupportedException(
                "Anthropic requires a reasoning token budget of at least 1024 tokens.");
        }
    }

    private static string ResolveEffort(ReasoningEffort effort)
    {
        if (effort == ReasoningEffort.Low)
        {
            return "low";
        }

        if (effort == ReasoningEffort.Medium)
        {
            return "medium";
        }

        if (effort == ReasoningEffort.High)
        {
            return "high";
        }

        if (effort == ReasoningEffort.Maximum)
        {
            return "max";
        }

        throw new NotSupportedException(
            $"Anthropic does not support reasoning effort '{effort.Value}'.");
    }

    private static void ReadBlock(JsonElement block, List<ChatItem> items)
    {
        switch (block.GetProperty("type").GetString())
        {
            case "text":
                items.Add(new ChatMessage(ChatRole.Assistant, block.GetProperty("text").GetString() ?? ""));
                break;
            case "thinking":
                var thinking = block.GetProperty("thinking").GetString() ?? "";
                items.Add(new ChatReasoningItem(new ReasoningContent(
                    [new ReasoningText(thinking, ReasoningTextKind.Trace)],
                    CreateState(block))));
                break;
            case "redacted_thinking":
                items.Add(new ChatReasoningItem(new ReasoningContent(state: CreateState(block))));
                break;
            case "tool_use":
                items.Add(new ToolCall(
                    block.GetProperty("id").GetString()!,
                    block.GetProperty("name").GetString()!,
                    block.GetProperty("input").GetRawText()));
                break;
        }
    }

    private static OpaqueReasoningState CreateState(JsonElement block) =>
        new(ReasoningStateFormat, Encoding.UTF8.GetBytes(block.GetRawText()));

    private static FinishReason ReadFinishReason(string? reason) => reason switch
    {
        "end_turn" or "stop_sequence" => FinishReason.Stop,
        "max_tokens" => FinishReason.Length,
        "tool_use" => FinishReason.ToolCalls,
        "refusal" => FinishReason.ContentFilter,
        null => FinishReason.Stop,
        _ => new FinishReason(reason)
    };

    private static TokenUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var input = usage.TryGetProperty("input_tokens", out var inputElement) ? inputElement.GetInt64() : 0;
        var output = usage.TryGetProperty("output_tokens", out var outputElement) ? outputElement.GetInt64() : 0;
        return new TokenUsage(input, output);
    }

    private sealed class StreamParser : IProtocolStreamParser
    {
        private readonly AnthropicCodec _codec;
        private readonly Dictionary<int, BlockAssembly> _blocks = [];
        private long _inputTokens;
        private long _outputTokens;
        private FinishReason _finishReason = FinishReason.Stop;

        public StreamParser(AnthropicCodec codec) => _codec = codec;

        public bool IsComplete { get; private set; }

        public IReadOnlyList<ChatStreamEvent> Parse(JsonElement root)
        {
            var events = new List<ChatStreamEvent>();
            var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            switch (type)
            {
                case "message_start":
                    ReadMessageStart(root);
                    break;
                case "content_block_start":
                    ReadBlockStart(root, events);
                    break;
                case "content_block_delta":
                    ReadBlockDelta(root, events);
                    break;
                case "content_block_stop":
                    ReadBlockStop(root, events);
                    break;
                case "message_delta":
                    ReadMessageDelta(root);
                    break;
                case "message_stop":
                    events.Add(new ChatUsageReceived(new TokenUsage(_inputTokens, _outputTokens)));
                    events.Add(new ChatCandidateCompleted(0, _finishReason));
                    IsComplete = true;
                    break;
                case "error":
                    var error = root.GetProperty("error");
                    throw _codec.CreateException(error.GetProperty("message").GetString() ?? "Anthropic stream failed.", errorCode: error.GetProperty("type").GetString());
            }

            return events;
        }

        private void ReadMessageStart(JsonElement root)
        {
            var usage = root.GetProperty("message").GetProperty("usage");
            _inputTokens = usage.TryGetProperty("input_tokens", out var input) ? input.GetInt64() : 0;
            _outputTokens = usage.TryGetProperty("output_tokens", out var output) ? output.GetInt64() : 0;
        }

        private void ReadBlockStart(JsonElement root, List<ChatStreamEvent> events)
        {
            var index = root.GetProperty("index").GetInt32();
            var block = root.GetProperty("content_block");
            var type = block.GetProperty("type").GetString() ?? "";
            var assembly = new BlockAssembly(type);
            _blocks[index] = assembly;
            if (type == "tool_use")
            {
                events.Add(new ChatToolCallDelta(0, index, block.GetProperty("id").GetString() ?? "", block.GetProperty("name").GetString() ?? "", ""));
            }
            else if (type == "redacted_thinking")
            {
                events.Add(new ChatReasoningStateReceived(0, index, CreateState(block)));
            }
        }

        private void ReadBlockDelta(JsonElement root, List<ChatStreamEvent> events)
        {
            var index = root.GetProperty("index").GetInt32();
            var delta = root.GetProperty("delta");
            var type = delta.GetProperty("type").GetString();
            var assembly = _blocks[index];
            switch (type)
            {
                case "text_delta":
                    events.Add(new ChatTextDelta(0, index, ChatRole.Assistant, delta.GetProperty("text").GetString() ?? ""));
                    break;
                case "thinking_delta":
                    var thinking = delta.GetProperty("thinking").GetString() ?? "";
                    assembly.Thinking.Append(thinking);
                    events.Add(new ChatReasoningTextDelta(0, index, 0, ReasoningTextKind.Trace, thinking));
                    break;
                case "signature_delta":
                    assembly.Signature = delta.GetProperty("signature").GetString() ?? "";
                    break;
                case "input_json_delta":
                    var partial = delta.GetProperty("partial_json").GetString() ?? "";
                    events.Add(new ChatToolCallDelta(0, index, "", "", partial));
                    break;
            }
        }

        private void ReadBlockStop(JsonElement root, List<ChatStreamEvent> events)
        {
            var index = root.GetProperty("index").GetInt32();
            var assembly = _blocks[index];
            if (assembly.Type == "thinking")
            {
                var raw = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "thinking",
                    thinking = assembly.Thinking.ToString(),
                    signature = assembly.Signature
                });
                events.Add(new ChatReasoningStateReceived(0, index, new OpaqueReasoningState(ReasoningStateFormat, raw)));
            }
        }

        private void ReadMessageDelta(JsonElement root)
        {
            var delta = root.GetProperty("delta");
            if (delta.TryGetProperty("stop_reason", out var stopReason))
            {
                _finishReason = ReadFinishReason(stopReason.GetString());
            }

            if (root.TryGetProperty("usage", out var usage)
                && usage.TryGetProperty("output_tokens", out var output))
            {
                _outputTokens = output.GetInt64();
            }
        }

        private sealed class BlockAssembly
        {
            public BlockAssembly(string type)
            {
                Type = type;
            }

            public string Type { get; }
            public StringBuilder Thinking { get; } = new();
            public string Signature { get; set; } = "";
        }
    }
}
