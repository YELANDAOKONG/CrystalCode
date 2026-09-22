using System.Text.Json;

using Crystal.Multimodal.Chat;

namespace CrystalCode.Providers.Protocol;

internal interface IMultimodalProtocolCodec
{
    string Path { get; }

    MultimodalChatCapabilities Capabilities { get; }

    byte[] WriteRequest(
        ProtocolOptions options,
        MultimodalChatRequest request,
        bool stream);

    void AddHeaders(HttpRequestMessage request, string apiKey);

    MultimodalChatResponse ReadResponse(JsonElement root);

    IMultimodalProtocolStreamParser CreateStreamParser();

    Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null);
}
