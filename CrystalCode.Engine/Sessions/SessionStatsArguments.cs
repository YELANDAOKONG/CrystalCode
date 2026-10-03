namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/stats</c> arguments.
/// </summary>
internal sealed record SessionStatsArguments(
    bool IncludeAllWorkspaces,
    int? WindowDays,
    int TopTools)
{
    public const int DefaultTopTools = 10;

    public static bool TryParse(
        string argument,
        out SessionStatsArguments parsed,
        out string error)
    {
        parsed = new SessionStatsArguments(false, null, DefaultTopTools);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return true;
        }

        IReadOnlyList<string> tokens;
        try
        {
            tokens = CommandArguments.Split(argument);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        var includeAll = false;
        int? windowDays = null;
        var topTools = DefaultTopTools;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                includeAll = true;
                continue;
            }

            if (token.Equals("tools", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= tokens.Count
                    || !int.TryParse(tokens[index + 1], out var count)
                    || count <= 0)
                {
                    error = "Pass a positive number after tools.";
                    return false;
                }

                topTools = count;
                index++;
                continue;
            }

            if (TryParseDayWindow(token, out var days))
            {
                windowDays = days;
                continue;
            }

            error = "Stats command supports: /stats, /stats all, /stats <Nd>, /stats tools <count>.";
            return false;
        }

        parsed = new SessionStatsArguments(includeAll, windowDays, topTools);
        return true;
    }

    private static bool TryParseDayWindow(string token, out int days)
    {
        days = 0;
        if (token.Length < 2
            || !token.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numeric = token[..^1];
        if (!int.TryParse(numeric, out days) || days <= 0)
        {
            return false;
        }

        return true;
    }
}
