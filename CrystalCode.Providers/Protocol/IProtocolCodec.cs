using System.Text.Json;

using Crystal.Chat;

namespace CrystalCode.Providers.Protocol;

internal interface IProtocolCodec
{
    string Path { get; }

    byte[] WriteRequest(ProtocolOptions options, ChatRequest request, bool stream);

    void AddHeaders(HttpRequestMessage request, string apiKey);

    ChatResponse ReadResponse(JsonElement root);

    IProtocolStreamParser CreateStreamParser();

    Exception CreateException(
        string message,
        int? statusCode = null,
        Exception? innerException = null,
        string? errorCode = null,
        TimeSpan? retryAfter = null);
}
