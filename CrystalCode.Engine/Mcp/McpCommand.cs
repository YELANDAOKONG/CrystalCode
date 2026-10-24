namespace CrystalCode.Engine.Mcp;

/// <summary>
/// One parsed <c>/mcp</c> command. A front end never sees this; the session
/// uses it so the grammar can be tested without a session.
/// </summary>
internal sealed record McpCommand(
    string Verb,
    string? Server = null,
    McpServerSource? Source = null,
    string? Policy = null)
{
    public const string UsageText =
        "MCP command must be /mcp, /mcp list, /mcp show <server>, /mcp on|off|reload, "
        + "/mcp enable|disable <server> [home|project], /mcp approval, "
        + "or /mcp home|project author|host.";

    public static bool TryParse(string argument, out McpCommand command)
    {
        ArgumentNullException.ThrowIfNull(argument);
        command = new McpCommand("status");
        var parts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return true;
        }

        var verb = parts[0].ToLowerInvariant();
        switch (verb)
        {
            case "list" or "on" or "off" or "reload" or "approval" when parts.Length == 1:
                command = new McpCommand(verb);
                return true;
            case "show" when parts.Length is 2 or 3:
            case "enable" or "disable" when parts.Length is 2 or 3:
                return TryServer(verb, parts, out command);
            case "home" or "project" when parts.Length == 1:
                command = new McpCommand("approval-show", Source: ParseSource(verb));
                return true;
            case "home" or "project" when parts.Length == 2
                && parts[1].ToLowerInvariant() is "author" or "host":
                command = new McpCommand("approval-set", Source: ParseSource(verb), Policy: parts[1].ToLowerInvariant());
                return true;
            default:
                return false;
        }
    }

    private static bool TryServer(string verb, string[] parts, out McpCommand command)
    {
        command = new McpCommand("status");
        McpServerSource? source = null;
        if (parts.Length == 3)
        {
            var named = parts[2].ToLowerInvariant();
            if (named is not ("home" or "project"))
            {
                return false;
            }

            source = ParseSource(named);
        }

        command = new McpCommand(verb, parts[1], source);
        return true;
    }

    private static McpServerSource ParseSource(string name) =>
        name == "home" ? McpServerSource.Home : McpServerSource.Project;
}
