using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Compatible;
using CrystalCode.Providers.Protocol;
using CrystalCode.Providers.Protocol.Interfaces;

namespace CrystalCode.Providers.Anthropic;

internal sealed class AnthropicCodec : IProtocolCodec
{
    internal const string ReasoningStateFormat = "anthropic.messages.thinking";
    private const int DefaultMaxTokens = 8192;
    private const string AnthropicVersion = "2023-06-01";
    private readonly string _vendorName;
    private readonly bool _summarizeThinking;

    public AnthropicCodec(string vendorName, bool summarizeThinking = true)
    {
        _vendorName = vendorName;
        _summarizeThinking = summarizeThinking;
    }

    public string Path => "messages";

    public void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        }
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
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
            WriteSystem(writer, request.Items);
            writer.WriteNumber("max_tokens", options.MaxTokens ?? DefaultMaxTokens);
            WriteMessages(writer, request.Items, _vendorName);
            WriteTools(writer, request.Tools);
            WriteReasoning(writer, request.Reasoning, _summarizeThinking);
            if (options.Temperature is { } temperature)
            {
                writer.WriteNumber("temperature", temperature);
            }

            if (options.TopP is { } topP)
            {
                writer.WriteNumber("top_p", topP);
            }

            ProcessingSlots.WriteSeparate(writer, options.ServiceTier, options.Speed);

            writer.WriteBoolean("stream", stream);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public ChatResponse ReadResponse(JsonElement root)
    {
        try
        {
            return ReadResponseBody(root);
        }
        catch (Exception exception) when (IsMalformedShape(exception))
        {
            throw CreateException($"{_vendorName} response was malformed.", innerException: exception);
        }
    }

    private ChatResponse ReadResponseBody(JsonElement root)
    {
        if (ProtocolFaults.TryCreate(
            root,
            _vendorName,
            "response",
            (message, code, inner) => CreateException(message, innerException: inner, errorCode: code),
            out var fault))
        {
            throw fault;
        }

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

    private Exception ReadStreamError(JsonElement root)
    {
        if (!CompatibleWire.TryReadError(root, out var code, out var providerMessage)
            || string.IsNullOrWhiteSpace(providerMessage))
        {
            return CreateException($"{_vendorName} stream failed.", errorCode: code);
        }

        return CreateException(providerMessage, errorCode: code);
    }

    private static void WriteSystem(Utf8JsonWriter writer, IReadOnlyList<ChatItem> items)
    {
        var systems = items.OfType<ChatMessage>()
            .Where(static message => message.Role == ChatRole.System)
            .Select(static message => message.Text)
            .ToArray();
        if (systems.Length == 0)
        {
            return;
        }

        writer.WritePropertyName("system");
        writer.WriteStartArray();
        for (var index = 0; index < systems.Length; index++)
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", systems[index]);
            // One breakpoint at the end of the system array caches the tool
            // definitions plus the system prefix, which stay stable in a session.
            if (index == systems.Length - 1)
            {
                WriteCacheControl(writer);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMessages(
        Utf8JsonWriter writer,
        IReadOnlyList<ChatItem> items,
        string vendorName)
    {
        writer.WritePropertyName("messages");
        writer.WriteStartArray();
        // A moving breakpoint on the final content block lets each round read
        // the conversation prefix written by the previous round.
        var lastBlockItem = LastBlockItem(items);
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
                WriteBlock(writer, block, ReferenceEquals(block, lastBlockItem), vendorName);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            blocks.Clear();
        }

        foreach (var item in items)
        {
            if (!CanWrite(item))
            {
                continue;
            }

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

    private static bool CanWrite(ChatItem item) =>
        item is not ChatReasoningItem reasoning
        || ReasoningReplay.IsReplayable(reasoning.Content, ReasoningStateFormat)
        || ReasoningReplay.ReadableText(reasoning.Content).Length > 0;

    private static ChatItem? LastBlockItem(IReadOnlyList<ChatItem> items)
    {
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (items[index] is ChatMessage message && message.Role == ChatRole.System)
            {
                continue;
            }

            if (!CanWrite(items[index]))
            {
                continue;
            }

            return items[index];
        }

        return null;
    }

    private static void WriteBlock(
        Utf8JsonWriter writer,
        ChatItem item,
        bool markCache,
        string vendorName)
    {
        switch (item)
        {
            case ChatMessage message:
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", message.Text);
                if (markCache)
                {
                    WriteCacheControl(writer);
                }

                writer.WriteEndObject();
                break;
            case ChatReasoningItem reasoning:
                WriteReasoningBlock(writer, reasoning.Content, markCache);
                break;
            case ToolCall call:
                ToolInputJson.RequireJsonCall(call, vendorName);
                writer.WriteStartObject();
                writer.WriteString("type", "tool_use");
                writer.WriteString("id", call.CallId);
                writer.WriteString("name", call.Name);
                writer.WritePropertyName("input");
                using (var document = JsonDocument.Parse(call.Arguments))
                {
                    document.RootElement.WriteTo(writer);
                }

                if (markCache)
                {
                    WriteCacheControl(writer);
                }

                writer.WriteEndObject();
                break;
            case ToolResult result:
                writer.WriteStartObject();
                writer.WriteString("type", "tool_result");
                writer.WriteString("tool_use_id", result.CallId);
                writer.WriteString("content", result.Text);
                writer.WriteBoolean("is_error", result.Status != ToolResultStatus.Success);
                if (markCache)
                {
                    WriteCacheControl(writer);
                }

                writer.WriteEndObject();
                break;
            default:
                throw new NotSupportedException(
                    $"Anthropic does not support chat item type {item.GetType().Name}.");
        }
    }

    private static void WriteCacheControl(Utf8JsonWriter writer)
    {
        writer.WritePropertyName("cache_control");
        writer.WriteStartObject();
        writer.WriteString("type", "ephemeral");
        writer.WriteEndObject();
    }

    private static void WriteReasoningBlock(
        Utf8JsonWriter writer,
        ReasoningContent content,
        bool markCache)
    {
        if (!ReasoningReplay.IsReplayable(content, ReasoningStateFormat))
        {
            // Another provider produced this block. Replay its readable text as
            // a plain text block so a model switch does not fail the turn.
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", ReasoningReplay.ReadableText(content));
            if (markCache)
            {
                WriteCacheControl(writer);
            }

            writer.WriteEndObject();
            return;
        }

        using var document = JsonDocument.Parse(content.State!.Data);
        document.RootElement.WriteTo(writer);
    }

    private void WriteTools(Utf8JsonWriter writer, IReadOnlyList<ToolDefinition> tools)
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
            ToolInputJson.RequireSchema(tool, _vendorName).WriteTo(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteReasoning(
        Utf8JsonWriter writer,
        ReasoningOptions? reasoning,
        bool summarizeThinking)
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

            // Opus 4.7 and later omit thinking text unless display asks for it.
            // summarized is the default. omitted leaves the field off.
            if (!disabled && summarizeThinking)
            {
                writer.WriteString("display", "summarized");
            }

            writer.WriteEndObject();
        }

        if (!disabled && reasoning.Effort is { } effort)
        {
            writer.WritePropertyName("output_config");
            writer.WriteStartObject();
            writer.WriteString("effort", effort.Value);
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
                    block.GetProperty("input").GetRawText(),
                    ToolInputKind.Json));
                break;
        }
    }

    // A gateway that drops a field or reorders events makes System.Text.Json
    // throw these. The protocol client only maps JsonException, so without this
    // the raw exception escapes as an unexplained failure.
    private static bool IsMalformedShape(Exception exception) =>
        exception is KeyNotFoundException or InvalidOperationException or FormatException;

    private static OpaqueReasoningState CreateState(JsonElement block) =>
        new(ReasoningStateFormat, Encoding.UTF8.GetBytes(block.GetRawText()));

    private static FinishReason ReadFinishReason(string? reason) => reason switch
    {
        "end_turn" or "stop_sequence" => FinishReason.Stop,
        // The response filled the context window rather than max_tokens, but
        // the reply is cut short the same way, so the session treats it alike.
        "max_tokens" or "model_context_window_exceeded" => FinishReason.Length,
        "tool_use" => FinishReason.ToolCalls,
        "refusal" => FinishReason.ContentFilter,
        null => FinishReason.Stop,
        _ => new FinishReason(reason)
    };

    private static TokenUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return null;
        }

        var tally = new AnthropicUsageTally();
        tally.Add(usage);
        return tally.ToUsage();
    }

    private sealed class StreamParser : IProtocolStreamParser
    {
        private readonly AnthropicCodec _codec;
        private readonly Dictionary<int, BlockAssembly> _blocks = [];
        private readonly AnthropicUsageTally _usage = new();

        // Stays null until a message_delta carries a stop_reason. Defaulting to
        // a normal stop would turn a stream that lost its final delta into a
        // finished answer.
        private FinishReason? _finishReason;

        // Usage events are cumulative snapshots; the last one wins. Sending one
        // as soon as the input side is known lets the session charge a request
        // whose stream breaks before message_stop.
        private TokenUsage? _reportedUsage;

        public StreamParser(AnthropicCodec codec) => _codec = codec;

        public bool IsComplete { get; private set; }

        public IReadOnlyList<ChatStreamEvent> Parse(JsonElement root)
        {
            var events = new List<ChatStreamEvent>();
            string? type = null;
            try
            {
                type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                switch (type)
                {
                    case "message_start":
                        ReadMessageStart(root, events);
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
                        ReadMessageDelta(root, events);
                        break;
                    case "message_stop":
                        ReadMessageStop(events);
                        break;
                    case "error":
                        throw _codec.ReadStreamError(root);
                    default:
                        break;
                }
            }
            catch (Exception exception) when (IsMalformedShape(exception))
            {
                throw _codec.CreateException(
                    $"{_codec._vendorName} stream contained a malformed {type ?? "unknown"} event.",
                    innerException: exception);
            }

            return events;
        }

        private void ReadMessageStart(JsonElement root, List<ChatStreamEvent> events)
        {
            if (root.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("usage", out var usage))
            {
                _usage.Add(usage);
                ReportUsage(events);
            }
        }

        private void ReadMessageStop(List<ChatStreamEvent> events)
        {
            if (_finishReason is not { } finishReason)
            {
                throw _codec.CreateException($"{_codec._vendorName} stream ended without a stop reason.");
            }

            ReportUsage(events);
            events.Add(new ChatCandidateCompleted(0, finishReason));
            IsComplete = true;
        }

        private void ReportUsage(List<ChatStreamEvent> events)
        {
            if (_usage.ToUsage() is { } usage && usage != _reportedUsage)
            {
                _reportedUsage = usage;
                events.Add(new ChatUsageReceived(usage));
            }
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
                events.Add(new ChatToolCallDelta(
                    0,
                    index,
                    block.GetProperty("id").GetString() ?? "",
                    block.GetProperty("name").GetString() ?? "",
                    "",
                    ToolInputKind.Json));
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
                    events.Add(new ChatToolCallDelta(
                        0,
                        index,
                        "",
                        "",
                        partial,
                        ToolInputKind.Json));
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

        private void ReadMessageDelta(JsonElement root, List<ChatStreamEvent> events)
        {
            // message_start carries "stop_reason": null, and some gateways repeat
            // that null here; only a string value ends the message.
            if (root.TryGetProperty("delta", out var delta)
                && delta.ValueKind == JsonValueKind.Object
                && delta.TryGetProperty("stop_reason", out var stopReason)
                && stopReason.ValueKind == JsonValueKind.String)
            {
                _finishReason = ReadFinishReason(stopReason.GetString());
            }

            if (root.TryGetProperty("usage", out var usage))
            {
                _usage.Add(usage);
                ReportUsage(events);
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
