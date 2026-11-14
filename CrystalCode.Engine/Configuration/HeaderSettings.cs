using System.Collections.ObjectModel;
using System.Text.Json;

namespace CrystalCode.Engine.Configuration;

internal static class HeaderSettings
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Proxy-Authorization", "Accept", "User-Agent", "Host",
        "OpenAI-Organization", "OpenAI-Project", "x-api-key", "x-goog-api-key",
        "anthropic-version", "Transfer-Encoding", "Connection"
    };

    public static IReadOnlyDictionary<string, string?> Read(JsonElement? json, string location, bool allowNull)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (json is null)
        {
            return Copy(values);
        }

        if (json.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{location} must be an object.");
        }

        foreach (var property in json.Value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String
                && !(allowNull && property.Value.ValueKind == JsonValueKind.Null))
            {
                throw new InvalidOperationException($"Invalid header value in {location}.");
            }

            if (!values.TryAdd(property.Name, property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString()))
            {
                throw new InvalidOperationException($"Duplicate header name in {location}.");
            }
        }

        return Copy(values, location);
    }

    public static IReadOnlyDictionary<string, string?> Copy(IReadOnlyDictionary<string, string?>? input, string location = "httpHeaders")
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var request = new HttpRequestMessage();
        foreach (var (name, value) in input ?? new Dictionary<string, string?>())
        {
            if (Reserved.Contains(name))
            {
                throw new InvalidOperationException($"Reserved header '{name}' in {location}.");
            }

            if (name.Length == 0 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c)))
                || value?.Any(c => c is '\r' or '\n' or '\0' || (c < 32 && c != '\t') || c == 127) == true
                || !TryAdd(request, name, value))
            {
                throw new InvalidOperationException($"Invalid header in {location}.");
            }

            request.Headers.Remove(name);
            if (!result.TryAdd(name, value))
            {
                throw new InvalidOperationException($"Duplicate header name in {location}.");
            }
        }

        return new ReadOnlyDictionary<string, string?>(result);
    }

    private static bool TryAdd(HttpRequestMessage request, string name, string? value)
    {
        try
        {
            return request.Headers.TryAddWithoutValidation(name, value ?? string.Empty);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static IReadOnlyDictionary<string, string?> Merge(IReadOnlyDictionary<string, string?> first, IReadOnlyDictionary<string, string?> second)
    {
        var result = new Dictionary<string, string?>(first, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in second)
        {
            result[name] = value;
        }

        return Copy(result);
    }

    public static IReadOnlyDictionary<string, string> Effective(IReadOnlyDictionary<string, string?> global, IReadOnlyDictionary<string, string?> provider)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in Merge(global, provider))
        {
            if (value is not null)
            {
                result[name] = value;
            }
        }

        return new ReadOnlyDictionary<string, string>(result);
    }

    public static JsonElement? Write(IReadOnlyDictionary<string, string?> headers) =>
        headers.Count == 0 ? null : JsonSerializer.SerializeToElement(headers);
}
