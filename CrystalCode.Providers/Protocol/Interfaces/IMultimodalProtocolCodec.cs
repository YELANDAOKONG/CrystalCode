using System.Text.Json;

using Crystal.Multimodal.Chat;
using CrystalCode.Plugins.Clients;

namespace CrystalCode.Providers.Protocol.Interfaces;
internal interface IMultimodalProtocolCodec
{
    string Path { get; }

    string GetPath(bool stream) => Path;

    bool UsesJsonLines => false;

    MultimodalChatCapabilities Capabilities { get; }

    byte[] WriteRequest(
        ProtocolOptions options,
        MultimodalChatRequest request,
        bool stream);

    IReadOnlyList<string> ReservedHeaders => [];

    void AddHeaders(HttpRequestMessage request, string apiKey);

    void AddRequestHeaders(HttpRequestMessage request, ProtocolOptions options) { }

    ProviderRequestUsage? ReadRequestUsage(JsonElement root, ProviderRequestUsage previous) => null;

    MultimodalChatResponse ReadResponse(JsonElement root);

    Func<JsonElement, MultimodalChatResponse?>? StreamCompletionReader => null;

    IMultimodalProtocolStreamParser CreateStreamParser();

    Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null);
}
