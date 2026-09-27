using System.Text.Json;

using CrystalCode.Sessions;

namespace CrystalCode.Home;

internal static class ExecutionBudgetMapper
{
    public static TurnLimits Read(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Null)
        {
            return TurnLimits.CreateDefault();
        }

        var value = element.Value;
        if (value.ValueKind is JsonValueKind.String
            && value.GetString() == "unlimited")
        {
            return TurnLimits.Unlimited;
        }

        if (value.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("Execution budget must be an object or 'unlimited'.");
        }

        int? maximumModelCalls = TurnLimits.DefaultMaximumModelCalls;
        int? maximumToolCalls = TurnLimits.DefaultMaximumToolCalls;
        TimeSpan? maximumDuration = TurnLimits.DefaultMaximumDuration;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new JsonException($"Duplicate execution budget setting '{property.Name}'.");
            }

            switch (property.Name)
            {
                case "maximumModelCalls":
                    maximumModelCalls = ReadCount(property.Value, property.Name);
                    break;
                case "maximumToolCalls":
                    maximumToolCalls = ReadCount(property.Value, property.Name);
                    break;
                case "maximumDurationSeconds":
                    maximumDuration = ReadDuration(property.Value);
                    break;
                default:
                    throw new JsonException($"Unknown execution budget setting '{property.Name}'.");
            }
        }

        return new TurnLimits(maximumModelCalls, maximumToolCalls, maximumDuration);
    }

    public static JsonElement? Write(TurnLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits == TurnLimits.CreateDefault())
        {
            return null;
        }

        if (limits == TurnLimits.Unlimited)
        {
            return JsonSerializer.SerializeToElement("unlimited");
        }

        return JsonSerializer.SerializeToElement(new
        {
            maximumModelCalls = limits.MaximumModelCalls,
            maximumToolCalls = limits.MaximumToolCalls,
            maximumDurationSeconds = limits.MaximumDuration?.TotalSeconds
        });
    }

    private static int? ReadCount(JsonElement value, string name)
    {
        if (value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.Number
            || !value.TryGetInt32(out var count))
        {
            throw new JsonException($"Execution budget '{name}' must be an integer or null.");
        }

        return count;
    }

    private static TimeSpan? ReadDuration(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.Number
            || !value.TryGetDouble(out var seconds)
            || !double.IsFinite(seconds)
            || seconds <= 0
            || seconds > TimeSpan.FromMilliseconds(uint.MaxValue - 1).TotalSeconds)
        {
            throw new JsonException(
                "Execution budget 'maximumDurationSeconds' must be a positive supported number or null.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
