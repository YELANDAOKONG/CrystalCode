using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Validates <c>/rules</c> arguments. An empty argument only displays the switches.
/// </summary>
public static class RuleFileArguments
{
    public const string Usage =
        "Rules command must be /rules, /rules on|off, /rules global on|off, or /rules project on|off.";

    public static bool TryParse(string argument, out RuleFileChange? change, out string error)
    {
        change = null;
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

        if (tokens.Count == 1 && IsSwitch(tokens[0], out var both))
        {
            change = new RuleFileChange(both, both);
            return true;
        }

        if (tokens.Count == 2
            && IsSide(tokens[0], out var global)
            && IsSwitch(tokens[1], out var enabled))
        {
            change = global
                ? new RuleFileChange(enabled, null)
                : new RuleFileChange(null, enabled);
            return true;
        }

        error = Usage;
        return false;
    }

    public static string Format(RuleFileSettings rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return "Rule files  global "
            + (rules.Global ? "on" : "off")
            + ", project "
            + (rules.Project ? "on" : "off");
    }

    private static bool IsSide(string token, out bool global)
    {
        if (token.Equals("global", StringComparison.OrdinalIgnoreCase))
        {
            global = true;
            return true;
        }

        if (token.Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            global = false;
            return true;
        }

        global = false;
        return false;
    }

    private static bool IsSwitch(string token, out bool enabled)
    {
        if (token.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        if (token.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }
}
