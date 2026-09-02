namespace CrystalCode.Sessions;

/// <summary>
/// Parses <c>/verbose</c> arguments.
/// </summary>
internal static class VerboseChangeArguments
{
    internal enum Target
    {
        Tools,
        Commands
    }

    public static bool TryParse(
        string argument,
        out Target? target,
        out bool? enabled,
        out string error)
    {
        target = null;
        enabled = null;
        error = string.Empty;
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

        if (tokens.Count == 0)
        {
            return true;
        }

        if (!TryParseTarget(tokens[0], out var parsedTarget))
        {
            error = "Verbose command must be /verbose, /verbose tools, or /verbose commands.";
            return false;
        }

        target = parsedTarget;
        if (tokens.Count == 1)
        {
            return true;
        }

        if (tokens.Count == 2 && TryParseToggle(tokens[1], out var toggle))
        {
            enabled = toggle;
            return true;
        }

        error = "Verbose command expects on or off after tools or commands.";
        return false;
    }

    private static bool TryParseTarget(string token, out Target target)
    {
        if (token.Equals("tools", StringComparison.OrdinalIgnoreCase))
        {
            target = Target.Tools;
            return true;
        }

        if (token.Equals("commands", StringComparison.OrdinalIgnoreCase))
        {
            target = Target.Commands;
            return true;
        }

        target = default;
        return false;
    }

    private static bool TryParseToggle(string token, out bool enabled)
    {
        enabled = false;
        if (token.Equals("on", StringComparison.OrdinalIgnoreCase)
            || token.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (token.Equals("off", StringComparison.OrdinalIgnoreCase)
            || token.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
