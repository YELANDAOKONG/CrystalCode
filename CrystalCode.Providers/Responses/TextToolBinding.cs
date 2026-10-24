using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Providers.Responses;

/// <summary>
/// Names a tool whose only parameter is one string. The Responses protocol
/// can offer such a tool as a free-form custom tool, so the model writes the
/// text directly instead of wrapping it in JSON. The host keeps the tool as a
/// JSON tool; the codec folds it on the way out and wraps it on the way in.
/// </summary>
/// <param name="ToolName">The model-facing tool name.</param>
/// <param name="Property">The one string property of the tool's schema.</param>
public sealed record TextToolBinding(string ToolName, string Property)
{
    /// <summary>
    /// True when the definition is a JSON tool with this name whose schema
    /// is an object with exactly that one required string property. A
    /// definition that differs, as after a replacement tool changed it,
    /// stays a function tool.
    /// </summary>
    internal bool Matches(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!string.Equals(tool.Name, ToolName, StringComparison.Ordinal)
            || tool.Input is not JsonToolInput json
            || json.Schema.ValueKind != JsonValueKind.Object
            || !json.Schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = properties.EnumerateObject().Select(static item => item.Name).ToArray();
        if (names.Length != 1 || !string.Equals(names[0], Property, StringComparison.Ordinal))
        {
            return false;
        }

        var kind = properties.GetProperty(Property);
        return kind.ValueKind == JsonValueKind.Object
            && kind.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "string";
    }

    /// <summary>
    /// Wraps the model's raw text as the JSON arguments the host tool reads.
    /// </summary>
    internal string Wrap(string text) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { [Property] = text });

    /// <summary>
    /// Reads the text back out of recorded JSON arguments. Returns false when
    /// the arguments are not an object holding that one string.
    /// </summary>
    internal bool TryUnwrap(string arguments, out string text)
    {
        text = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(Property, out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            text = value.GetString() ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
