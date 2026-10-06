using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Reads and writes the <c>enabled</c> boolean on one JSON object.
/// Other properties stay in place. A value that is not a boolean is refused.
/// </summary>
internal static class JsonEnabledField
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static bool TryRead(string json, out bool enabled, out JsonObject? document, out string error)
    {
        enabled = true;
        document = null;
        error = string.Empty;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            error = "manifest is not valid JSON.";
            return false;
        }

        if (node is not JsonObject root)
        {
            error = "manifest must be an object.";
            return false;
        }

        document = root;
        if (!root.TryGetPropertyValue("enabled", out var value) || value is null)
        {
            return true;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<bool>(out var flag))
        {
            document = null;
            error = "enabled must be true or false.";
            return false;
        }

        enabled = flag;
        return true;
    }

    public static void Write(string path, JsonObject document, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        document["enabled"] = JsonValue.Create(enabled);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, document.ToJsonString(Indented) + Environment.NewLine);
        File.Move(temporary, path, overwrite: true);
    }
}
