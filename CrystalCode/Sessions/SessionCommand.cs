namespace CrystalCode.Sessions;

/// <summary>
/// One slash command parsed from the prompt.
/// </summary>
public sealed record SessionCommand(SessionVerb Verb, string Argument)
{
    public static bool TryParse(string input, out SessionCommand command)
    {
        command = new SessionCommand(SessionVerb.None, string.Empty);
        if (string.IsNullOrWhiteSpace(input) || input[0] != '/')
        {
            return false;
        }

        var trimmed = input.Trim();
        var space = trimmed.IndexOf(' ');
        var verb = space < 0 ? trimmed : trimmed[..space];
        var argument = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
        if (SlashCatalog.TryMatch(verb, out var matched))
        {
            command = new SessionCommand(matched, argument);
            return true;
        }

        command = new SessionCommand(SessionVerb.Unknown, trimmed);
        return true;
    }
}
