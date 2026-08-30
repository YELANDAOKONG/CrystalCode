namespace CrystalHarness.Sessions;

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
        command = verb.ToLowerInvariant() switch
        {
            "/help" or "/h" => new SessionCommand(SessionVerb.Help, argument),
            "/plan" => new SessionCommand(SessionVerb.Plan, argument),
            "/approval" => new SessionCommand(SessionVerb.Approval, argument),
            "/status" => new SessionCommand(SessionVerb.Status, argument),
            "/clear" => new SessionCommand(SessionVerb.Clear, argument),
            "/cd" => new SessionCommand(SessionVerb.Cd, argument),
            "/quit" or "/exit" or "/q" => new SessionCommand(SessionVerb.Quit, argument),
            _ => new SessionCommand(SessionVerb.Unknown, trimmed)
        };
        return true;
    }
}
