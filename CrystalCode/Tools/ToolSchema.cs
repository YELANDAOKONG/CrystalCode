using System.Text.Json;

namespace CrystalCode.Tools;

internal static class ToolSchema
{
    public static JsonElement Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
