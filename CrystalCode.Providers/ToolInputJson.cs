using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Providers;

internal static class ToolInputJson
{
    public static void RequireJsonCall(
        ToolCall call,
        string vendorName)
    {
        ArgumentNullException.ThrowIfNull(call);
        RequireJsonCall(call.InputKind, vendorName);
    }

    public static void RequireJsonCall(
        ToolInputKind inputKind,
        string vendorName)
    {
        ArgumentNullException.ThrowIfNull(inputKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);

        if (inputKind == ToolInputKind.Text)
        {
            throw new NotSupportedException(
                $"{vendorName} does not support text tool input.");
        }
    }

    /// <summary>
    /// Returns the call's arguments as JSON text. A call with no parameters
    /// can carry empty text, which no JSON parser accepts, so it reads as the
    /// empty object.
    /// </summary>
    public static string ArgumentsOrEmptyObject(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments;
    }

    public static JsonElement RequireSchema(
        ToolDefinition tool,
        string vendorName)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorName);

        if (tool.Input is JsonToolInput json)
        {
            return json.Schema;
        }

        throw new NotSupportedException(
            $"{vendorName} does not support text tool input.");
    }
}
