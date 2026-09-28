using System.Text.Json;
using System.Text.Json.Nodes;

using Crystal;
using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Compatible;
using CrystalCode.Providers.Protocol;

namespace CrystalCode.Providers.Ollama;

internal sealed class OllamaMultimodalCodec : IMultimodalProtocolCodec
{
    private readonly OllamaCodec _text = new();

    public string Path => _text.Path;

    public bool UsesJsonLines => true;

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
            ?? throw new JsonException("Ollama request body was invalid.");
        foreach (var messageNode in root["messages"]!.AsArray())
        {
            var message = messageNode!.AsObject();
            if (message["content"] is not JsonValue content
                || !content.TryGetValue<string>(out var text))
            {
                continue;
            }

            var matches = bridge.Images
                .Where(pair => text.Contains(pair.Key, StringComparison.Ordinal))
                .OrderBy(pair => text.IndexOf(pair.Key, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                continue;
            }

            if (message["role"]?.GetValue<string>() != "user")
            {
                throw new NotSupportedException("Ollama images are supported only in user messages.");
            }

            var images = new JsonArray();
            foreach (var (token, image) in matches)
            {
                if (image.Image.Source is not InlineMediaSource inline)
                {
                    throw new NotSupportedException("Ollama images must use inline bytes.");
                }

                images.Add(Convert.ToBase64String(inline.Data.Span));
                text = text.Replace(token, string.Empty, StringComparison.Ordinal);
            }

            message["content"] = text;
            message["images"] = images;
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
}
