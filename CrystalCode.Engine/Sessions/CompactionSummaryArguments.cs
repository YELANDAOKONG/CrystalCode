namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/compact summary</c> and <c>/compact auto</c>. A bare command toggles.
/// <c>automatic</c> is the same command as <c>auto</c>.
/// </summary>
internal static class CompactionSummaryArguments
{
    internal readonly record struct Request(bool Automatic, bool? Enabled);

    public static bool IsSummaryCommand(string? argument) =>
        IsCommand(argument, "summary");

    public static bool IsAutomaticCommand(string? argument) =>
        IsCommand(argument, "automatic") || IsCommand(argument, "auto");

    public static bool TryParse(string argument, bool automatic, out Request request, out string error)
    {
        request = default;
        error = string.Empty;
        var name = automatic ? AutomaticName(argument) : "summary";
        var rest = argument.Trim()[name.Length..].Trim();
        if (rest.Length == 0)
        {
            request = new Request(automatic, Enabled: null);
            return true;
        }

        if (rest.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            request = new Request(automatic, true);
            return true;
        }

        if (rest.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            request = new Request(automatic, false);
            return true;
        }

        error = automatic
            ? "Automatic compaction summary is on or off."
            : "Compaction summary is on or off.";
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

    private static string AutomaticName(string argument)
    {
        var trimmed = argument.Trim();
        return trimmed.StartsWith("automatic", StringComparison.OrdinalIgnoreCase)
            ? "automatic"
            : "auto";
    }
}
