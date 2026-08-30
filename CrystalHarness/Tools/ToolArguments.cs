using System.Text.Json;

namespace CrystalHarness.Tools;

internal static class ToolArguments
{
    public static bool TryReadString(string arguments, string name, out string value)
    {
        value = string.Empty;
        if (!TryOpen(arguments, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty(name, out var property)
                || property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = property.GetString();
            if (text is null)
            {
                return false;
            }

            value = text;
            return true;
        }
    }

    public static bool TryReadRequiredString(string arguments, string name, out string value) =>
        TryReadString(arguments, name, out value) && !string.IsNullOrWhiteSpace(value);

    public static bool TryReadOptionalString(string arguments, string name, out string? value)
    {
        value = null;
        if (!TryOpen(arguments, out var document))
        {
            return true;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty(name, out var property))
            {
                return true;
            }

            if (property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = property.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            value = text;
            return true;
        }
    }

    public static bool TryReadOptionalInt32(string arguments, string name, out int? value)
    {
        value = null;
        if (!TryOpen(arguments, out var document))
        {
            return true;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty(name, out var property))
            {
                return true;
            }

            if (property.ValueKind != JsonValueKind.Number
                || !property.TryGetInt32(out var number))
            {
                return false;
            }

            value = number;
            return true;
        }
    }

    public static bool TryReadOptionalBoolean(string arguments, string name, out bool? value)
    {
        value = null;
        if (!TryOpen(arguments, out var document))
        {
            return true;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty(name, out var property))
            {
                return true;
            }

            switch (property.ValueKind)
            {
                case JsonValueKind.True:
                    value = true;
                    return true;
                case JsonValueKind.False:
                    value = false;
                    return true;
                default:
                    return false;
            }
        }
    }

    public static bool TryReadOptionalStringArray(
        string arguments,
        string name,
        out IReadOnlyList<string>? values)
    {
        values = null;
        if (!TryOpen(arguments, out var document))
        {
            return true;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty(name, out var property))
            {
                return true;
            }

            if (property.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var items = new List<string>();
            foreach (var element in property.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                var text = element.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                items.Add(text);
            }

            values = items;
            return true;
        }
    }

    private static bool TryOpen(string arguments, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null!;
            return false;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }
}
