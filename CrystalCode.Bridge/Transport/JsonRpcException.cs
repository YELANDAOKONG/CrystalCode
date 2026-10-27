using System.Text.Json;

namespace CrystalCode.Bridge.Transport;

/// <summary>
/// A request that ends with a JSON-RPC error. <see cref="Kind"/> is the
/// Crystal error kind written under <c>error.data.crystal.kind</c>;
/// <see cref="Data"/> writes further fields into that same object.
/// </summary>
internal sealed class JsonRpcException : Exception
{
    public JsonRpcException(int code, string message, string? kind = null, Action<Utf8JsonWriter>? data = null)
        : base(message)
    {
        Code = code;
        Kind = kind;
        Data = data;
    }

    public int Code { get; }

    public string? Kind { get; }

    public new Action<Utf8JsonWriter>? Data { get; }

    public static JsonRpcException InvalidParams(string message) =>
        new(JsonRpcErrorCode.InvalidParams, message);

    public static JsonRpcException NotFound(string message) =>
        new(JsonRpcErrorCode.ResourceNotFound, message);

    public static JsonRpcException Refused(string kind, string message, Action<Utf8JsonWriter>? data = null) =>
        new(JsonRpcErrorCode.InternalError, message, kind, data);
}
