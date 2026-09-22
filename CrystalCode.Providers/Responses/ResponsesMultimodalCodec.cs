using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Responses;

internal sealed class ResponsesMultimodalCodec : IMultimodalProtocolCodec
{
    private readonly ResponsesCodec _textCodec;
    private readonly string _vendorName;

    public ResponsesMultimodalCodec(string vendorName)
    {
        _vendorName = vendorName;
        _textCodec = new ResponsesCodec(vendorName);
    }

    public string Path => _textCodec.Path;

    public MultimodalChatCapabilities Capabilities { get; } = new(
        [
            new MultimodalContentCapability(ContentModality.Text),
            new MultimodalContentCapability(
                ContentModality.Image,
                [MediaSourceKind.Inline, MediaSourceKind.Uri])
        ],
        [new MultimodalContentCapability(ContentModality.Text)],
        supportsTools: true,
        supportsReasoningOptions: true);

    public void AddHeaders(HttpRequestMessage request, string apiKey) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    public byte[] WriteRequest(
        ProtocolOptions options,
        MultimodalChatRequest request,
        bool stream)
    {
        if (request.Items.Count == 0)
        {
            throw new ArgumentException(
                $"{_vendorName} requires at least one transcript item.",
                nameof(request));
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

    public MultimodalChatResponse ReadResponse(JsonElement root) =>
        Convert(_textCodec.ReadResponse(root));

    public IMultimodalProtocolStreamParser CreateStreamParser() =>
        new StreamParser(_textCodec.CreateStreamParser());

    public Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null) =>
        _textCodec.CreateException(
            message,
            statusCode,
            innerException,
            errorCode,
            retryAfter);

    private static void WriteItem(Utf8JsonWriter writer, MultimodalChatItem item)
    {
        switch (item)
        {
            case MultimodalMessage message:
                WriteMessage(writer, message);
                break;
            case MultimodalReasoningItem reasoning:
                WriteReasoningItem(writer, reasoning.Content);
                break;
            case MultimodalToolCall toolCall:
                WriteToolCall(writer, toolCall);
                break;
            case MultimodalToolResult toolResult:
                WriteToolResult(writer, toolResult);
                break;
            default:
                throw new NotSupportedException(
                    $"Responses does not support multimodal item type {item.GetType().Name}.");
        }
    }

    private static void WriteMessage(Utf8JsonWriter writer, MultimodalMessage message)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteString("role", message.Role.Value);
        writer.WritePropertyName("content");
        writer.WriteStartArray();
        foreach (var content in message.Contents)
        {
            WriteMessageContent(writer, message.Role, content);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMessageContent(
        Utf8JsonWriter writer,
        MultimodalChatRole role,
        MultimodalContent content)
    {
        writer.WriteStartObject();
        switch (content)
        {
            case TextContent text:
                writer.WriteString(
                    "type",
                    role == MultimodalChatRole.Assistant ? "output_text" : "input_text");
                writer.WriteString("text", text.Text);
                break;
            case ImageContent image when role != MultimodalChatRole.Assistant:
                writer.WriteString("type", "input_image");
                writer.WriteString("image_url", ImageUrl(image.Image));
                break;
            case ImageContent:
                throw new NotSupportedException(
                    "Responses image output replay is not supported.");
            default:
                throw new NotSupportedException(
                    $"Responses does not support {content.Modality.Value} message input.");
        }

        writer.WriteEndObject();
    }

    private static void WriteReasoningItem(
        Utf8JsonWriter writer,
        MultimodalReasoningContent content)
    {
        if (content.State is null
            || content.State.Format != ResponsesCodec.ReasoningStateFormat)
        {
            throw new NotSupportedException(
                "Responses requires its opaque reasoning item for replay.");
        }

        using var document = JsonDocument.Parse(content.State.Data);
        document.RootElement.WriteTo(writer);
    }

    private static void WriteToolCall(Utf8JsonWriter writer, MultimodalToolCall call)
    {
        if (call.Contents.Count != 0)
        {
            throw new NotSupportedException(
                "Responses multimodal tool-call content is not supported.");
        }

        writer.WriteStartObject();
        writer.WriteString("type", "function_call");
        writer.WriteString("call_id", call.CallId);
        writer.WriteString("name", call.Name);
        writer.WriteString("arguments", call.Arguments);
        writer.WriteEndObject();
    }

    private static void WriteToolResult(
        Utf8JsonWriter writer,
        MultimodalToolResult result)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "function_call_output");
        writer.WriteString("call_id", result.CallId);
        writer.WritePropertyName("output");
        writer.WriteStartArray();
        foreach (var content in result.Contents)
        {
            writer.WriteStartObject();
            switch (content)
            {
                case TextContent text:
                    writer.WriteString("type", "input_text");
                    writer.WriteString("text", text.Text);
                    break;
                case ImageContent image:
                    writer.WriteString("type", "input_image");
                    writer.WriteString("image_url", ImageUrl(image.Image));
                    break;
                default:
                    throw new NotSupportedException(
                        $"Responses does not support {content.Modality.Value} tool output.");
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string ImageUrl(ImageMedia image) => image.Source switch
    {
        InlineMediaSource inline =>
            $"data:{image.MimeType.Value};base64,{System.Convert.ToBase64String(inline.Data.Span)}",
        UriMediaSource uri when uri.Uri.Scheme is "http" or "https" =>
            uri.Uri.AbsoluteUri,
        UriMediaSource => throw new NotSupportedException(
            "Responses image URIs must use HTTP or HTTPS."),
        _ => throw new NotSupportedException(
            $"Responses does not support image source {image.Source.Kind.Value}.")
    };

    private static void WriteTools(
        Utf8JsonWriter writer,
        IReadOnlyList<ToolDefinition> tools)
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

    private static void WriteReasoning(
        Utf8JsonWriter writer,
        ReasoningOptions? reasoning)
    {
        if (reasoning is null)
        {
            return;
        }

        if (reasoning.TokenBudget is not null)
        {
            throw new NotSupportedException(
                "Responses does not support a reasoning token budget.");
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
            writer.WriteString(
                "effort",
                effort == ReasoningEffort.Maximum ? "xhigh" : effort.Value);
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
            throw new NotSupportedException(
                $"Responses does not support reasoning mode '{mode.Value}'.");
        }

        if (reasoning.Output is { } output
            && output != ReasoningOutput.None
            && output != ReasoningOutput.Summary)
        {
            throw new NotSupportedException(
                $"Responses does not support reasoning output '{output.Value}'.");
        }

        if (reasoning.Mode == ReasoningMode.Disabled
            && reasoning.Output == ReasoningOutput.Summary)
        {
            throw new NotSupportedException(
                "Responses cannot disable reasoning while requesting a reasoning summary.");
        }

        if (reasoning.Mode == ReasoningMode.Enabled
            && reasoning.Output == ReasoningOutput.None)
        {
            throw new NotSupportedException(
                "Responses cannot enable reasoning while requesting no reasoning output.");
        }

        if ((reasoning.Mode == ReasoningMode.Disabled
                || reasoning.Output == ReasoningOutput.None)
            && reasoning.Effort is not null)
        {
            throw new NotSupportedException(
                "Responses does not apply reasoning effort when reasoning is disabled.");
        }
    }

    private static MultimodalChatResponse Convert(ChatResponse response) =>
        new(
            response.Candidates.Select(static candidate =>
                new MultimodalChatCandidate(
                    candidate.Items.Select(ConvertItem),
                    candidate.FinishReason)),
            response.Usage);

    private static MultimodalChatItem ConvertItem(ChatItem item) => item switch
    {
        ChatMessage message => new MultimodalMessage(
            new MultimodalChatRole(message.Role.Value),
            [new TextContent(message.Text)]),
        ChatReasoningItem reasoning => new MultimodalReasoningItem(
            new MultimodalReasoningContent(
                reasoning.Content.TextSegments.Select(static text =>
                    new MultimodalReasoningPart(
                        new TextContent(text.Text),
                        MultimodalReasoningKind.Summary)),
                reasoning.Content.State)),
        ToolCall call => new MultimodalToolCall(
            call.CallId,
            call.Name,
            call.Arguments),
        ToolResult result => new MultimodalToolResult(
            result.CallId,
            [new TextContent(result.Text)],
            result.Status == ToolResultStatus.Success
                ? MultimodalToolResultStatus.Success
                : MultimodalToolResultStatus.Failure),
        _ => throw new NotSupportedException(
            $"Responses does not support chat item type {item.GetType().Name}.")
    };

    private sealed class StreamParser : IMultimodalProtocolStreamParser
    {
        private readonly IProtocolStreamParser _inner;
        private readonly HashSet<int> _startedMessages = [];

        public StreamParser(IProtocolStreamParser inner) => _inner = inner;

        public bool IsComplete => _inner.IsComplete;

        public IReadOnlyList<MultimodalChatStreamEvent> Parse(JsonElement root)
        {
            var result = new List<MultimodalChatStreamEvent>();
            foreach (var streamEvent in _inner.Parse(root))
            {
                if (streamEvent is ChatTextDelta text
                    && _startedMessages.Add(text.ItemIndex))
                {
                    result.Add(new MultimodalMessageStarted(
                        text.CandidateIndex,
                        text.ItemIndex,
                        MultimodalChatRole.Assistant));
                }

                result.Add(ConvertEvent(streamEvent));
            }

            return result;
        }

        private static MultimodalChatStreamEvent ConvertEvent(
            ChatStreamEvent streamEvent) => streamEvent switch
        {
            ChatTextDelta text => new MultimodalMessageTextDelta(
                text.CandidateIndex,
                text.ItemIndex,
                0,
                text.Text),
            ChatReasoningTextDelta reasoning => new MultimodalReasoningTextDelta(
                reasoning.CandidateIndex,
                reasoning.ItemIndex,
                reasoning.TextSegmentIndex,
                MultimodalReasoningKind.Summary,
                reasoning.Text),
            ChatReasoningStateReceived reasoning =>
                new MultimodalReasoningStateReceived(
                    reasoning.CandidateIndex,
                    reasoning.ItemIndex,
                    reasoning.State),
            ChatToolCallDelta tool => new MultimodalToolCallDelta(
                tool.CandidateIndex,
                tool.ItemIndex,
                tool.CallIdDelta,
                tool.NameDelta,
                tool.ArgumentsDelta),
            ChatCandidateCompleted completed => new MultimodalChatCandidateCompleted(
                completed.CandidateIndex,
                completed.FinishReason),
            ChatUsageReceived usage => new MultimodalChatUsageReceived(usage.Usage),
            _ => throw new NotSupportedException(
                $"Responses does not support stream event {streamEvent.GetType().Name}.")
        };
    }
}
