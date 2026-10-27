namespace CrystalCode.Bridge.Transport;

/// <summary>
/// JSON-RPC error codes Bridge sends. Only codes whose ACP meaning fits are
/// used. <c>-32000</c> (authentication required) is never sent, and no other
/// code in the reserved <c>-32000</c> to <c>-32099</c> range is used.
/// </summary>
internal static class JsonRpcErrorCode
{
    public const int ParseError = -32700;

    public const int InvalidRequest = -32600;

    public const int MethodNotFound = -32601;

    public const int InvalidParams = -32602;

    public const int InternalError = -32603;

    public const int ResourceNotFound = -32002;

    public const int RequestCancelled = -32800;
}
