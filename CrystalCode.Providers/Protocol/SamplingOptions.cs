using System.Text.Json;

namespace CrystalCode.Providers.Protocol;

internal static class SamplingOptions
{
    public static (double? Temperature, double? TopP) Read(
        JsonElement modelOptions, double? temperature, double? topP)
    {
        if (modelOptions.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Model options must be a JSON object.", nameof(modelOptions));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in modelOptions.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new ArgumentException("Model options contain a duplicate key.", nameof(modelOptions));
            }

            if (property.Name.Equals("temperature", StringComparison.OrdinalIgnoreCase))
            {
                temperature = ReadNumber(property.Value, "temperature");
            }
            else if (property.Name.Equals("topP", StringComparison.OrdinalIgnoreCase))
            {
                topP = ReadNumber(property.Value, "topP");
            }
            else
            {
                throw new ArgumentException(
                    "The adapter supports only temperature and topP in model options.", nameof(modelOptions));
            }
        }

        return (temperature, topP);
    }

    private static double? ReadNumber(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
        {
            return number;
        }

        throw new ArgumentException($"Model option '{name}' must be a finite number or null.", nameof(value));
    }
}
