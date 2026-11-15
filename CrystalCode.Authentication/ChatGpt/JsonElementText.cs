using System.Text.Json;

namespace CrystalCode.Authentication.ChatGpt;

internal static class JsonElementText
{
    public static string? GetStringOrNull(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
