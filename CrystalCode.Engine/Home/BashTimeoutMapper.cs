using System.Text.Json;

using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

internal static class BashTimeoutMapper
{
    public const string PropertyName = "bashTimeoutSeconds";

    public const string BackgroundPropertyName = "backgroundBashTimeoutSeconds";

    public static int? Read(JsonElement root, string propertyName, int? defaultValue)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return defaultValue;
        }

        var specified = false;
        var value = default(JsonElement);
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (specified)
            {
                throw new JsonException($"Duplicate bash timeout setting '{propertyName}'.");
            }

            specified = true;
            value = property.Value;
        }

        if (!specified)
        {
            return defaultValue;
        }

        return ReadValue(value, propertyName);
    }

    public static JsonElement? Write(int? seconds, int? defaultValue)
    {
        if (seconds == defaultValue)
        {
            return null;
        }

        if (seconds is null)
        {
            return JsonSerializer.SerializeToElement("unlimited");
        }

        return JsonSerializer.SerializeToElement(seconds.Value);
    }

    private static int? ReadValue(JsonElement value, string propertyName)
    {
        if (value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.String
            && value.GetString() == "unlimited")
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.Number
            && value.TryGetInt32(out var seconds)
            && BashTool.IsSupported(seconds))
        {
            return seconds;
        }

        throw new JsonException(
            $"{propertyName} must be a positive supported integer, null, or \"unlimited\".");
    }
}
