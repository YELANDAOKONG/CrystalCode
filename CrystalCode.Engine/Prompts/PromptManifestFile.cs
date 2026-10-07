using System.Text.Json;
using System.Text.Json.Nodes;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Reads and updates <c>prompt.json</c>. Unknown fields stay in place.
/// A missing <c>enabled</c> value means false, so a new directory stays off.
/// </summary>
internal static class PromptManifestFile
{
    public const string FileName = "prompt.json";

    public const int MaximumTitleLength = 80;

    public const int MaximumDescriptionLength = 280;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string PathFor(string directory) => Path.Combine(directory, FileName);

    public static bool TryRead(string directory, out PromptManifest? manifest, out string error)
    {
        manifest = null;
        error = string.Empty;
        var path = PathFor(directory);
        if (!File.Exists(path))
        {
            error = "prompt.json is missing.";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "prompt.json could not be read.";
            return false;
        }

        return TryParse(json, out manifest, out error);
    }

    public static bool TrySetEnabled(string directory, bool enabled, out string error)
    {
        if (!TryLoadObject(directory, out var document, out error) || document is null)
        {
            return false;
        }

        document["enabled"] = JsonValue.Create(enabled);
        return TryWrite(PathFor(directory), document, out error);
    }

    public static bool TrySetOrder(string directory, int order, out string error)
    {
        if (order < 0)
        {
            error = "order must be a non-negative integer.";
            return false;
        }

        if (!TryLoadObject(directory, out var document, out error) || document is null)
        {
            return false;
        }

        document["order"] = JsonValue.Create(order);
        return TryWrite(PathFor(directory), document, out error);
    }

    public static bool TryParse(string json, out PromptManifest? manifest, out string error)
    {
        manifest = null;
        error = string.Empty;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            error = "prompt.json is not valid JSON.";
            return false;
        }

        if (node is not JsonObject root)
        {
            error = "prompt.json must be an object.";
            return false;
        }

        if (!TryText(root, "name", MaximumTitleLength, requiredText: true, out var title, out error))
        {
            return false;
        }

        if (!TryText(root, "description", MaximumDescriptionLength, requiredText: false, out var description, out error))
        {
            return false;
        }

        if (!TryEnabled(root, out var enabled, out error))
        {
            return false;
        }

        if (!TryOrder(root, out var order, out error))
        {
            return false;
        }

        manifest = new PromptManifest(title, description, enabled, order);
        return true;
    }

    private static bool TryLoadObject(string directory, out JsonObject? document, out string error)
    {
        document = null;
        error = string.Empty;
        var path = PathFor(directory);
        if (!File.Exists(path))
        {
            error = "prompt.json is missing.";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "prompt.json could not be read.";
            return false;
        }

        if (!TryParse(json, out _, out error))
        {
            return false;
        }

        try
        {
            document = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            error = "prompt.json is not valid JSON.";
            return false;
        }

        if (document is null)
        {
            error = "prompt.json must be an object.";
            return false;
        }

        return true;
    }

    private static bool TryWrite(string path, JsonObject document, out string error)
    {
        error = string.Empty;
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, document.ToJsonString(Indented) + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "prompt.json could not be written.";
            return false;
        }
    }

    private static bool TryText(
        JsonObject root,
        string property,
        int maximum,
        bool requiredText,
        out string text,
        out string error)
    {
        text = string.Empty;
        error = string.Empty;
        if (!root.TryGetPropertyValue(property, out var value) || value is null)
        {
            return true;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var raw))
        {
            error = property + " must be text.";
            return false;
        }

        text = raw.Trim();
        if (requiredText && text.Length == 0)
        {
            error = property + " must not be empty.";
            return false;
        }

        if (text.Length > maximum)
        {
            error = property + " is too long.";
            return false;
        }

        return true;
    }

    private static bool TryEnabled(JsonObject root, out bool enabled, out string error)
    {
        enabled = false;
        error = string.Empty;
        if (!root.TryGetPropertyValue("enabled", out var value) || value is null)
        {
            return true;
        }

        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<bool>(out var flag))
        {
            error = "enabled must be true or false.";
            return false;
        }

        enabled = flag;
        return true;
    }

    private static bool TryOrder(JsonObject root, out int? order, out string error)
    {
        order = null;
        error = string.Empty;
        if (!root.TryGetPropertyValue("order", out var value) || value is null)
        {
            return true;
        }

        if (value is not JsonValue jsonValue
            || !jsonValue.TryGetValue<int>(out var number)
            || number < 0)
        {
            error = "order must be a non-negative integer.";
            return false;
        }

        order = number;
        return true;
    }
}
