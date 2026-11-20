using System.Collections.Immutable;
using System.Text.Json;

namespace CrystalCode.Providers.Protocol;

internal static class RequestUsageFields
{
    public static JsonElement Object(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Object
            ? value : default;

    public static long? Count(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var count)
        && count >= 0
            ? count : null;

    public static ImmutableDictionary<int, long>? CacheWrites(
        JsonElement details, params (string Field, int LifetimeSeconds)[] fields)
    {
        if (details.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var values = ImmutableDictionary.CreateBuilder<int, long>();
        foreach (var (field, lifetimeSeconds) in fields)
        {
            if (Count(details, field) is { } tokens)
            {
                values.Add(lifetimeSeconds, tokens);
            }
        }

        return values.Count == 0 ? null : values.ToImmutable();
    }
}
