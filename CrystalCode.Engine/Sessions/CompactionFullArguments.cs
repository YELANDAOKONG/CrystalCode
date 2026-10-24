namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/compact full</c>. It takes no further argument.
/// </summary>
internal static class CompactionFullArguments
{
    public static bool IsFullCommand(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var trimmed = argument.Trim();
        return trimmed.Equals("full", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("full ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("full\t", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string argument, out string error)
    {
        error = string.Empty;
        if (argument.Trim()["full".Length..].Trim().Length == 0)
        {
            return true;
        }

        error = "Full compaction takes no argument.";
        return false;
    }
}
