using System.Text.Json;
using System.Text.Json.Nodes;

using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Compatible;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Gemini;

internal sealed class GeminiMultimodalCodec : IMultimodalProtocolCodec
{
    private readonly GeminiCodec _text;

    public GeminiMultimodalCodec(string model) => _text = new GeminiCodec(model);

    public string Path => _text.Path;

    public string GetPath(bool stream) => _text.GetPath(stream);

    public MultimodalChatCapabilities Capabilities { get; } = new(
        [
            new MultimodalContentCapability(ContentModality.Text),
            new MultimodalContentCapability(ContentModality.Image, [MediaSourceKind.Inline])
        ],
        [new MultimodalContentCapability(ContentModality.Text)],
        supportsTools: true,
        supportsReasoningOptions: true);

    public void AddHeaders(HttpRequestMessage request, string apiKey) =>
        _text.AddHeaders(request, apiKey);

    public byte[] WriteRequest(
        ProtocolOptions options,
        MultimodalChatRequest request,
        bool stream)
    {
        var bridge = new NativeMultimodalRequest();
        var body = _text.WriteRequest(options, bridge.Convert(request), stream);
        if (bridge.Images.Count == 0)
        {
            return body;
        }

        var root = JsonNode.Parse(body)?.AsObject()
            ?? throw new JsonException("Gemini request body was invalid.");
        foreach (var contentNode in root["contents"]!.AsArray())
        {
            var content = contentNode!.AsObject();
            var parts = content["parts"]!.AsArray();
            for (var index = parts.Count - 1; index >= 0; index--)
            {
                var part = parts[index]!.AsObject();
                if (part["functionResponse"] is not null)
                {
                    var result = part["functionResponse"]?["response"]?["result"]?.GetValue<string>();
                    if (result is not null && bridge.Images.Keys.Any(
                        token => result.Contains(token, StringComparison.Ordinal)))
                    {
                        throw new NotSupportedException(
                            "Gemini tool-result images are not supported by this adapter.");
                    }
                }

                if (part["text"] is not JsonValue textValue
                    || !textValue.TryGetValue<string>(out var text))
                {
                    continue;
                }

                var expanded = Expand(text, bridge.Images);
                if (expanded.Count == 0)
                {
                    continue;
                }

                parts.RemoveAt(index);
                for (var itemIndex = expanded.Count - 1; itemIndex >= 0; itemIndex--)
                {
                    var replacement = expanded[itemIndex];
                    expanded.RemoveAt(itemIndex);
                    parts.Insert(index, replacement);
                }
            }
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    public MultimodalChatResponse ReadResponse(JsonElement root) =>
        CompatibleMultimodalOutput.Convert(_text.ReadResponse(root));

    public IMultimodalProtocolStreamParser CreateStreamParser() =>
        new NativeMultimodalStreamParser(_text.CreateStreamParser());

    public Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null) =>
        _text.CreateException(message, statusCode, innerException, errorCode, retryAfter);

    private static JsonArray Expand(
        string text,
        IReadOnlyDictionary<string, ImageContent> images)
    {
        var matches = images
            .Where(pair => text.Contains(pair.Key, StringComparison.Ordinal))
            .OrderBy(pair => text.IndexOf(pair.Key, StringComparison.Ordinal))
            .ToArray();
        var parts = new JsonArray();
        var cursor = 0;
        foreach (var (token, image) in matches)
        {
            var index = text.IndexOf(token, cursor, StringComparison.Ordinal);
            if (index > cursor)
            {
                parts.Add(new JsonObject { ["text"] = text[cursor..index] });
            }

            if (image.Image.Source is not InlineMediaSource inline)
            {
                throw new NotSupportedException("Gemini images must use inline bytes.");
            }

            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject
                {
                    ["mimeType"] = image.Image.MimeType.Value,
                    ["data"] = Convert.ToBase64String(inline.Data.Span)
                }
            });
            cursor = index + token.Length;
        }

        if (cursor < text.Length && matches.Length > 0)
        {
            parts.Add(new JsonObject { ["text"] = text[cursor..] });
        }

        return parts;
    }
}
