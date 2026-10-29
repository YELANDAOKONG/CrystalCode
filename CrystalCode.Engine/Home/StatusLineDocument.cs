using System.Text.Json;

using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Reads and writes the <c>statusLine</c> and <c>statusLineDrop</c> entries of
/// <c>config.json</c>. A <c>statusLine</c> entry is a field name or an object
/// <c>{"field", "keep", "hideBelow"}</c>.
/// </summary>
internal static class StatusLineDocument
{
    public static StatusLineSettings Read(
        bool enabled,
        JsonElement? fields,
        IReadOnlyList<string>? dropOrder)
    {
        var names = new List<string>();
        var rules = new List<StatusLineRule>();
        if (fields is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                switch (entry.ValueKind)
                {
                    case JsonValueKind.String:
                        names.Add(entry.GetString() ?? string.Empty);
                        break;
                    case JsonValueKind.Object:
                        var rule = ReadRule(entry);
                        names.Add(rule.Field);
                        rules.Add(rule);
                        break;
                    default:
                        throw new JsonException("statusLine entries must be a field name or an object.");
                }
            }
        }
        else if (fields is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) })
        {
            throw new JsonException("statusLine must be an array.");
        }

        return new StatusLineSettings(enabled, names, rules, dropOrder);
    }

    public static JsonElement? WriteFields(StatusLineSettings settings)
    {
        if (settings.Fields.SequenceEqual(StatusLineSettings.DefaultFields, StringComparer.Ordinal)
            && settings.Rules.Count == 0)
        {
            return null;
        }

        var entries = settings.Fields.Select(name =>
        {
            var rule = settings.Rules.FirstOrDefault(candidate => candidate.Field == name);
            if (rule is null)
            {
                return (object)name;
            }

            var entry = new Dictionary<string, object> { ["field"] = name };
            if (rule.Keep)
            {
                entry["keep"] = true;
            }

            if (rule.HideBelow is { } width)
            {
                entry["hideBelow"] = width;
            }

            return entry;
        }).ToArray();
        return JsonSerializer.SerializeToElement(entries, HomeJson.Options);
    }

    private static StatusLineRule ReadRule(JsonElement entry)
    {
        string? field = null;
        var keep = false;
        int? hideBelow = null;
        foreach (var property in entry.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "field" when property.Value.ValueKind == JsonValueKind.String:
                    field = property.Value.GetString();
                    break;
                case "keep" when property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    keep = property.Value.GetBoolean();
                    break;
                case "hidebelow" when property.Value.TryGetInt32(out var width):
                    hideBelow = width;
                    break;
                default:
                    throw new JsonException($"statusLine entry has an invalid '{property.Name}' property.");
            }
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            throw new JsonException("statusLine object entries need a field name.");
        }

        return new StatusLineRule(field.Trim().ToLowerInvariant(), keep, hideBelow);
    }
}
