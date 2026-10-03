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
                if (part["functionResponse"] is JsonObject functionResponse)
                {
                    AttachFunctionImages(functionResponse, bridge.Images);
                    continue;
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

    private static void AttachFunctionImages(
        JsonObject functionResponse,
        IReadOnlyDictionary<string, ImageContent> images)
    {
        if (functionResponse["response"] is not JsonObject response)
        {
            return;
        }

        var key = ResponseTextKey(response);
        if (key is null
            || response[key] is not JsonValue value
            || !value.TryGetValue<string>(out var text))
        {
            return;
        }

        var taken = TakeImages(text, images);
        if (taken.Images.Count == 0)
        {
            return;
        }

        response[key] = taken.Text;
        var parts = new JsonArray();
        foreach (var image in taken.Images)
        {
            parts.Add(InlineData(image));
        }

        functionResponse["parts"] = parts;
    }

    private static string? ResponseTextKey(JsonObject response)
    {
        if (response["result"] is JsonValue)
        {
            return "result";
        }

        if (response["error"] is JsonValue)
        {
            return "error";
        }

        return null;
    }

    private readonly record struct TakenImages(string Text, IReadOnlyList<ImageContent> Images);

    private static TakenImages TakeImages(
        string text,
        IReadOnlyDictionary<string, ImageContent> images)
    {
        var matches = images
            .Where(pair => text.Contains(pair.Key, StringComparison.Ordinal))
            .OrderBy(pair => text.IndexOf(pair.Key, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return new TakenImages(text, []);
        }

        var kept = text;
        var found = new List<ImageContent>(matches.Length);
        foreach (var (token, image) in matches)
        {
            found.Add(image);
            kept = kept.Replace(token, string.Empty, StringComparison.Ordinal);
        }

        return new TakenImages(kept, found);
    }

    private static JsonObject InlineData(ImageContent image)
    {
        if (image.Image.Source is not InlineMediaSource inline)
        {
            throw new NotSupportedException("Gemini images must use inline bytes.");
        }

        return new JsonObject
        {
            ["inlineData"] = new JsonObject
            {
                ["mimeType"] = image.Image.MimeType.Value,
                ["data"] = Convert.ToBase64String(inline.Data.Span)
            }
        };
    }

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
