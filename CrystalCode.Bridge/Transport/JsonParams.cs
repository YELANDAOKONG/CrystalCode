using System.Text.Json;

namespace CrystalCode.Bridge.Transport;

/// <summary>Reads request fields; a missing or mistyped required field is <c>-32602</c>.</summary>
internal static class JsonParams
{
    public static string RequiredString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw JsonRpcException.InvalidParams(name + " must be a string.");

    public static string? OptionalString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The array at <paramref name="name"/>, or null when it is absent or null.</summary>
    public static JsonElement? OptionalArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Array
            ? value
            : throw JsonRpcException.InvalidParams(name + " must be an array.");
    }
}
