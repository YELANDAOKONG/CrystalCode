using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Providers;

internal static class ToolInputJson
{
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
