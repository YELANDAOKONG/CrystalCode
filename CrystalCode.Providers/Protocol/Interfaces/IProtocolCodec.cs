using System.Text.Json;

using Crystal.Chat;
using CrystalCode.Plugins.Clients;

namespace CrystalCode.Providers.Protocol.Interfaces;
internal interface IProtocolCodec
{
    string Path { get; }

    string GetPath(bool stream) => Path;

    bool UsesJsonLines => false;

    byte[] WriteRequest(ProtocolOptions options, ChatRequest request, bool stream);

    IReadOnlyList<string> ReservedHeaders => [];

    void AddHeaders(HttpRequestMessage request, string apiKey);

    void AddRequestHeaders(HttpRequestMessage request, ProtocolOptions options) { }

    ProviderRequestUsage? ReadRequestUsage(JsonElement root, ProviderRequestUsage previous) => null;

    ChatResponse ReadResponse(JsonElement root);

    Func<JsonElement, ChatResponse?>? StreamCompletionReader => null;

    IProtocolStreamParser CreateStreamParser();

    Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null);
}
