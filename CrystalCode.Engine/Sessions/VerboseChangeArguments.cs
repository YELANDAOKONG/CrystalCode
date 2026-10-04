namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/verbose</c> arguments.
/// </summary>
internal static class VerboseChangeArguments
{
    public static bool TryParse(
        string argument,
        out VerboseTarget? target,
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
            error = "Verbose command must be /verbose, /verbose tools, /verbose commands, or /verbose approvals.";
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

        error = "Verbose command expects on or off after tools, commands, or approvals.";
        return false;
    }

    private static bool TryParseTarget(string token, out VerboseTarget target)
    {
        if (token.Equals("tools", StringComparison.OrdinalIgnoreCase))
        {
            target = VerboseTarget.Tools;
            return true;
        }

        if (token.Equals("commands", StringComparison.OrdinalIgnoreCase))
        {
            target = VerboseTarget.Commands;
            return true;
        }

        if (token.Equals("approvals", StringComparison.OrdinalIgnoreCase))
        {
            target = VerboseTarget.Approvals;
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
