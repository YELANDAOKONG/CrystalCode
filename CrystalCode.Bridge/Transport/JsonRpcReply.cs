using System.Text.Json;

namespace CrystalCode.Bridge.Transport;

/// <summary>
/// The result of one request. <see cref="Write"/> writes the complete
/// <c>result</c> value. <see cref="AfterSend"/> runs once the response is
/// queued for output, so anything it sends follows the response.
/// </summary>
internal sealed record JsonRpcReply(Action<Utf8JsonWriter> Write, Action? AfterSend = null)
{
    public static JsonRpcReply Empty { get; } = new(static writer =>
    {
        writer.WriteStartObject();
        writer.WriteEndObject();
    });
}
