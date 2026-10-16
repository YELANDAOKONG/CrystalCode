namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/compact micro</c>. A bare command toggles. <c>on</c> and <c>off</c> set it.
/// </summary>
internal static class CompactionMicroArguments
{
    public static bool IsMicroCommand(string? argument) =>
        IsCommand(argument, "micro");

    public static bool TryParse(string argument, out bool? enabled, out string error)
    {
        enabled = null;
        error = string.Empty;
        var rest = argument.Trim()["micro".Length..].Trim();
        if (rest.Length == 0)
        {
            return true;
        }

        if (rest.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (rest.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        error = "Microcompaction is on or off.";
        return false;
    }

    private static bool IsCommand(string? argument, string name)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var trimmed = argument.Trim();
        return trimmed.Equals(name, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(name + "\t", StringComparison.OrdinalIgnoreCase);
    }
}
