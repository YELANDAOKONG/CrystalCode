namespace CrystalCode.Run;

/// <summary>
/// Stdout shape for <c>crystal run</c>. <c>default</c> is plain text.
/// <c>json</c> is one JSON object per line.
/// </summary>
internal static class RunFormat
{
    public const string Default = "default";

    public const string Json = "json";

    public static bool TryParse(string? value, out string format, out string error)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Trim().Equals(Default, StringComparison.OrdinalIgnoreCase))
        {
            format = Default;
            error = string.Empty;
            return true;
        }

        if (value.Trim().Equals(Json, StringComparison.OrdinalIgnoreCase))
        {
            format = Json;
            error = string.Empty;
            return true;
        }

        format = Default;
        error = "Format must be default or json.";
        return false;
    }
}
