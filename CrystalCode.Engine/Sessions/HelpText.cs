namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Plain-text command help for a front end without terminal keys: the
/// built-in slash verbs with their aliases, then plugin and MCP prompt verbs.
/// </summary>
public static class HelpText
{
    public static string Format(IReadOnlyList<SlashCompletion> pluginCommands)
    {
        ArgumentNullException.ThrowIfNull(pluginCommands);
        var lines = new List<string> { "Commands" };
        foreach (var spec in SlashCatalog.BuiltIn)
        {
            var names = "/" + spec.Name;
            if (spec.Aliases.Count > 0)
            {
                names += "  " + string.Join("  ", spec.Aliases.Select(static alias => "/" + alias));
            }

            lines.Add($"{names,-28}{spec.Help}");
        }

        foreach (var command in pluginCommands)
        {
            var help = string.IsNullOrWhiteSpace(command.Help) ? command.Name : command.Help;
            lines.Add($"/{command.Name,-27}{help}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
