using System.Text.Json;

using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Reads and writes the endpoint <c>options</c> object. The document shape is
/// an open object of JSON scalars. Recognized names are interpreted per protocol.
/// </summary>
internal static class EndpointOptionsReader
{
    public static ThinkingDisplay? Read(
        string provider,
        ProviderProtocol protocol,
        JsonElement? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(protocol);
        if (options is null
            || options.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (options.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Provider '{provider}' options must be an object.");
        }

        ThinkingDisplay? display = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in options.Value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new InvalidOperationException(
                    $"Provider '{provider}' option '{property.Name}' is listed more than once.");
            }

            if (IsModelSampling(property.Name))
            {
                throw new InvalidOperationException(
                    $"Provider '{provider}' option '{property.Name}' is not an endpoint option. Set it on the model.");
            }

            if (!IsScalar(property.Value.ValueKind))
            {
                throw new InvalidOperationException(
                    $"Provider '{provider}' option '{property.Name}' must be a JSON scalar.");
            }

            if (property.Name.Equals("thinkingDisplay", StringComparison.OrdinalIgnoreCase))
            {
                if (protocol != ProviderProtocol.Anthropic)
                {
                    throw new InvalidOperationException(
                        $"Option 'thinkingDisplay' is not valid for protocol '{protocol.Value}' on provider '{provider}'.");
                }

                display = ReadThinkingDisplay(provider, property.Value);
                continue;
            }

            throw new InvalidOperationException(
                $"Option '{property.Name}' is not valid for protocol '{protocol.Value}' on provider '{provider}'.");
        }

        return display == ThinkingDisplay.Summarized ? null : display;
    }

    public static JsonElement? Write(ThinkingDisplay? display)
    {
        if (display != ThinkingDisplay.Omitted)
        {
            return null;
        }

        return JsonSerializer.SerializeToElement(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["thinkingDisplay"] = ThinkingDisplay.Omitted.Value
            },
            HomeJson.Options);
    }

    private static ThinkingDisplay ReadThinkingDisplay(string provider, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Provider '{provider}' option 'thinkingDisplay' must be summarized or omitted.");
        }

        try
        {
            return ThinkingDisplay.Parse(value.GetString() ?? "");
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Provider '{provider}' option 'thinkingDisplay' must be summarized or omitted.",
                exception);
        }
    }

    private static bool IsModelSampling(string name) =>
        name.Equals("temperature", StringComparison.OrdinalIgnoreCase)
        || name.Equals("topP", StringComparison.OrdinalIgnoreCase)
        || name.Equals("maxTokens", StringComparison.OrdinalIgnoreCase);

    private static bool IsScalar(JsonValueKind kind) =>
        kind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
}
