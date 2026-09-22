using System.Text.Json;
using System.Text.Json.Nodes;

using Crystal;
using Crystal.Chat;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using Crystal.Multimodal.Tools;
using Crystal.Reasoning;
using Crystal.Tools;
using CrystalCode.Providers.Compatible;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Anthropic;

internal sealed class AnthropicMultimodalCodec : IMultimodalProtocolCodec
{
    private readonly AnthropicCodec _textCodec;

    public AnthropicMultimodalCodec(string vendorName)
    {
        _textCodec = new AnthropicCodec(vendorName);
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

    public byte[] WriteRequest(
        ProtocolOptions options,
        MultimodalChatRequest request,
        bool stream)
    {
        ArgumentNullException.ThrowIfNull(request);
        var images = new Dictionary<string, ImageContent>(StringComparer.Ordinal);
        var items = request.Items.Select(item => ConvertItem(item, images)).ToArray();
        var bytes = _textCodec.WriteRequest(
            options,
            new ChatRequest(items, request.Tools, request.Reasoning),
            stream);
        if (images.Count == 0)
        {
            return bytes;
        }

        var root = JsonNode.Parse(bytes)?.AsObject()
            ?? throw new JsonException("Anthropic request body was not a JSON object.");
        var messages = root["messages"]?.AsArray()
            ?? throw new JsonException("Anthropic request body was missing messages.");
        foreach (var messageNode in messages)
        {
            var blocks = messageNode?["content"]?.AsArray();
            if (blocks is null)
            {
                continue;
            }

            ExpandBlocks(blocks, images);
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    public void AddHeaders(HttpRequestMessage request, string apiKey) =>
        _textCodec.AddHeaders(request, apiKey);

    public MultimodalChatResponse ReadResponse(JsonElement root) =>
        CompatibleMultimodalOutput.Convert(_textCodec.ReadResponse(root));

    public IMultimodalProtocolStreamParser CreateStreamParser() =>
        new StreamParser(_textCodec.CreateStreamParser());

    public Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null) =>
        _textCodec.CreateException(message, statusCode, innerException, errorCode, retryAfter);

    private static ChatItem ConvertItem(
        MultimodalChatItem item,
        IDictionary<string, ImageContent> images) => item switch
    {
        MultimodalMessage message => new ChatMessage(
            new ChatRole(message.Role.Value),
            FlattenContents(message.Role, message.Contents, images)),
        MultimodalReasoningItem reasoning => new ChatReasoningItem(
            new ReasoningContent(
                reasoning.Content.Parts.Select(ConvertReasoningPart),
                reasoning.Content.State)),
        MultimodalToolCall call when call.Contents.Count == 0 =>
            new ToolCall(call.CallId, call.Name, call.Arguments),
        MultimodalToolCall => throw new NotSupportedException(
            "Anthropic does not support multimodal tool-call content."),
        MultimodalToolResult result => new ToolResult(
            result.CallId,
            FlattenContents(null, result.Contents, images),
            result.Status == MultimodalToolResultStatus.Success
                ? ToolResultStatus.Success
                : ToolResultStatus.Failure),
        _ => throw new NotSupportedException(
            $"Anthropic does not support {item.GetType().Name}.")
    };

    private static ReasoningText ConvertReasoningPart(MultimodalReasoningPart part)
    {
        if (part.Content is not TextContent text)
        {
            throw new NotSupportedException(
                $"Anthropic does not support {part.Content.Modality.Value} reasoning output replay.");
        }

        return new ReasoningText(
            text.Text,
            part.Kind == MultimodalReasoningKind.Trace
                ? ReasoningTextKind.Trace
                : ReasoningTextKind.Summary);
    }

    private static string FlattenContents(
        MultimodalChatRole? role,
        IReadOnlyList<MultimodalContent> contents,
        IDictionary<string, ImageContent> images)
    {
        var text = new System.Text.StringBuilder();
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent value:
                    text.Append(value.Text);
                    break;
                case ImageContent image when role is null || role == MultimodalChatRole.User:
                    var token = $"\u001fcrystal-image-{Guid.NewGuid():N}\u001f";
                    images.Add(token, image);
                    text.Append(token);
                    break;
                case ImageContent:
                    throw new NotSupportedException(
                        "Anthropic accepts images only in user and tool messages.");
                default:
                    throw new NotSupportedException(
                        $"Anthropic does not support {content.Modality.Value} input.");
            }
        }

        return text.ToString();
    }

    private static void ExpandBlocks(
        JsonArray blocks,
        IReadOnlyDictionary<string, ImageContent> images)
    {
        for (var index = blocks.Count - 1; index >= 0; index--)
        {
            var block = blocks[index]?.AsObject();
            var type = block?["type"]?.GetValue<string>();
            if (type == "text" && block?["text"] is JsonValue textValue
                && textValue.TryGetValue<string>(out var text))
            {
                ReplaceBlock(blocks, index, Expand(text, images));
            }
            else if (type == "tool_result" && block?["content"] is JsonValue contentValue
                && contentValue.TryGetValue<string>(out var content))
            {
                var expanded = Expand(content, images);
                if (expanded.Count > 0)
                {
                    block["content"] = expanded;
                }
            }
        }
    }

    private static void ReplaceBlock(JsonArray blocks, int index, JsonArray replacement)
    {
        if (replacement.Count == 0)
        {
            return;
        }

        blocks.RemoveAt(index);
        for (var replacementIndex = replacement.Count - 1; replacementIndex >= 0; replacementIndex--)
        {
            var node = replacement[replacementIndex];
            replacement.RemoveAt(replacementIndex);
            blocks.Insert(index, node);
        }
    }

    private static JsonArray Expand(
        string text,
        IReadOnlyDictionary<string, ImageContent> images)
    {
        var matches = images
            .Where(pair => text.Contains(pair.Key, StringComparison.Ordinal))
            .OrderBy(pair => text.IndexOf(pair.Key, StringComparison.Ordinal))
            .ToArray();
        var blocks = new JsonArray();
        if (matches.Length == 0)
        {
            return blocks;
        }

        var cursor = 0;
        foreach (var (token, image) in matches)
        {
            var index = text.IndexOf(token, cursor, StringComparison.Ordinal);
            if (index > cursor)
            {
                blocks.Add(TextBlock(text[cursor..index]));
            }

            blocks.Add(ImageBlock(image.Image));
            cursor = index + token.Length;
        }

        if (cursor < text.Length)
        {
            blocks.Add(TextBlock(text[cursor..]));
        }

        return blocks;
    }

    private static JsonObject TextBlock(string text) => new()
    {
        ["type"] = "text",
        ["text"] = text
    };

    private static JsonObject ImageBlock(ImageMedia image) => image.Source switch
    {
        InlineMediaSource inline => new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = image.MimeType.Value,
                ["data"] = Convert.ToBase64String(inline.Data.Span)
            }
        },
        UriMediaSource uri when uri.Uri.Scheme is "http" or "https" => new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "url",
                ["url"] = uri.Uri.AbsoluteUri
            }
        },
        UriMediaSource => throw new NotSupportedException(
            "Anthropic image URIs must be HTTP(S)."),
        _ => throw new NotSupportedException(
            $"Anthropic does not support image source {image.Source.Kind.Value}.")
    };

    private sealed class StreamParser : IMultimodalProtocolStreamParser
    {
        private readonly IProtocolStreamParser _parser;
        private readonly CompatibleMultimodalOutput _output = new();

        public StreamParser(IProtocolStreamParser parser)
        {
            _parser = parser;
        }

        public bool IsComplete => _parser.IsComplete;

        public IReadOnlyList<MultimodalChatStreamEvent> Parse(JsonElement root)
        {
            var result = new List<MultimodalChatStreamEvent>();
            foreach (var streamEvent in _parser.Parse(root))
            {
                result.AddRange(_output.Convert(streamEvent));
            }

            return result;
        }
    }
}
