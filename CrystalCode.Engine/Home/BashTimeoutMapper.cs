using System.Text.Json;

using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Home;

internal static class BashTimeoutMapper
{
    public static int? Read(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return WorkspaceLimits.BashTimeoutSeconds;
        }

        var specified = false;
        var value = default(JsonElement);
        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals("bashTimeoutSeconds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (specified)
            {
                throw new JsonException("Duplicate bash timeout setting 'bashTimeoutSeconds'.");
            }

            specified = true;
            value = property.Value;
        }

        if (!specified)
        {
            return WorkspaceLimits.BashTimeoutSeconds;
        }

        return ReadValue(value);
    }

    public static JsonElement? Write(int? seconds)
    {
        if (seconds == WorkspaceLimits.BashTimeoutSeconds)
        {
            return null;
        }

        if (seconds is null)
        {
            return JsonSerializer.SerializeToElement("unlimited");
        }

        return JsonSerializer.SerializeToElement(seconds.Value);
    }

    private static int? ReadValue(JsonElement value)
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
            "bashTimeoutSeconds must be a positive supported integer, null, or \"unlimited\".");
    }
}
