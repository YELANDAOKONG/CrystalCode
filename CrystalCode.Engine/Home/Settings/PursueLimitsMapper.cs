using System.Text.Json;

using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home.Settings;

/// <summary>
/// Reads and writes <c>pursueLimits</c>. An omitted field keeps its default,
/// <c>null</c> is unlimited, and the string <c>"unlimited"</c> unlimits every
/// field except the stall count.
/// </summary>
internal static class PursueLimitsMapper
{
    public static PursueLimits Read(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null)
        {
            return PursueLimits.Default;
        }

        var value = element.Value;
        if (value.ValueKind is JsonValueKind.String && value.GetString() == "unlimited")
        {
            return new PursueLimits(null, PursueLimits.DefaultMaximumStalls, null, null, null, null);
        }

        if (value.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("pursueLimits must be an object or 'unlimited'.");
        }

        var limits = PursueLimits.Default;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException($"Duplicate pursueLimits setting '{property.Name}'.");
            }

            var name = property.Name switch
            {
                "maximumContinuations" => "continuations",
                "maximumStalls" => "stalls",
                "maximumModelCalls" => "model-calls",
                "maximumToolCalls" => "tool-calls",
                "maximumDurationSeconds" => "duration",
                "maximumWaitSeconds" => "wait",
                _ => throw new JsonException($"Unknown pursueLimits setting '{property.Name}'.")
            };
            limits = limits.With(name, ReadCount(property.Value, property.Name));
        }

        return limits;
    }

    public static JsonElement? Write(PursueLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits == PursueLimits.Default)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(new
        {
            maximumContinuations = limits.MaximumContinuations,
            maximumStalls = limits.MaximumStalls,
            maximumModelCalls = limits.MaximumModelCalls,
            maximumToolCalls = limits.MaximumToolCalls,
            maximumDurationSeconds = limits.MaximumDurationSeconds,
            maximumWaitSeconds = limits.MaximumWaitSeconds
        });
    }

    private static int? ReadCount(JsonElement value, string name)
    {
        if (value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.Number || !value.TryGetInt32(out var count))
        {
            throw new JsonException($"pursueLimits '{name}' must be an integer or null.");
        }

        return count;
    }
}
