namespace CrystalCode.Sessions;

/// <summary>
/// Parses <c>/export</c> argument tokens.
/// </summary>
public static class ExportSessionArguments
{
    public static bool TryParse(
        IReadOnlyList<string> tokens,
        out ExportSessionOptions options,
        out string error)
    {
        options = new ExportSessionOptions(string.Empty, null, false);
        error = string.Empty;
        if (tokens.Count == 0)
        {
            return false;
        }

        var format = tokens[0].ToLowerInvariant();
        if (format is not ("markdown" or "md" or "json"))
        {
            error = "Unknown export format  " + tokens[0];
            return false;
        }

        if (format == "md")
        {
            format = "markdown";
        }

        var includeSystem = false;
        string? explicitPath = null;
        for (var i = 1; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i], "--system", StringComparison.OrdinalIgnoreCase))
            {
                includeSystem = true;
                continue;
            }

            if (tokens[i].StartsWith("--", StringComparison.Ordinal))
            {
                error = "Unknown export flag  " + tokens[i];
                return false;
            }

            if (explicitPath is not null)
            {
                error = "Export accepts at most one path.";
                return false;
            }

            explicitPath = tokens[i];
        }

        options = new ExportSessionOptions(format, explicitPath, includeSystem);
        return true;
    }
}
