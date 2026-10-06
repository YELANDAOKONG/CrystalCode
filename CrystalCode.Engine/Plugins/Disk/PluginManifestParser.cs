using System.Text.Json;

using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Reads one plugin.json. Unknown fields are ignored.</summary>
internal static class PluginManifestParser
{
    public static bool TryParse(
        string directory,
        string json,
        out PluginManifest? manifest,
        out string error)
    {
        manifest = null;
        error = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "plugin.json must be an object.";
                return false;
            }

            var enabled = true;
            if (root.TryGetProperty("enabled", out var enabledValue))
            {
                if (enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "enabled must be true or false.";
                    return false;
                }

                enabled = enabledValue.GetBoolean();
            }

            if (!TryString(root, "assembly", out var assembly))
            {
                error = "assembly is required.";
                return false;
            }

            if (!TryString(root, "type", out var typeName) || typeName.Contains('/') || typeName.Contains('\\'))
            {
                error = "type is required.";
                return false;
            }

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(directory, assembly));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                error = "assembly path is invalid.";
                return false;
            }

            if (!ExternalPath.IsInside(directory, full))
            {
                error = "assembly must stay inside the plugin directory.";
                return false;
            }

            manifest = new PluginManifest(full, typeName, enabled);
            return true;
        }
        catch (JsonException)
        {
            error = "plugin.json is not valid JSON.";
            return false;
        }
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }
}
