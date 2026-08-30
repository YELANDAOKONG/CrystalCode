namespace CrystalHarness.Sessions;

/// <summary>
/// Built-in slash verbs, including aliases such as /new for /clear.
/// </summary>
public static class SlashCatalog
{
    public static IReadOnlyList<SlashSpec> BuiltIn { get; } =
    [
        new(SessionVerb.Help, "help", ["h"], "shortcuts and commands"),
        new(SessionVerb.Plan, "plan", [], "toggle Plan / Work"),
        new(SessionVerb.Approval, "approval", [], "default | autoedit | review | full"),
        new(SessionVerb.Status, "status", [], "turns, tokens, mode"),
        new(SessionVerb.Clear, "clear", ["new"], "new conversation"),
        new(SessionVerb.Cd, "cd", [], "show or set workspace"),
        new(SessionVerb.Resume, "resume", ["continue", "sessions"], "replay latest or id"),
        new(SessionVerb.Quit, "quit", ["exit", "q"], "exit")
    ];

    public static bool TryMatch(string verb, out SessionVerb matched)
    {
        matched = SessionVerb.Unknown;
        if (string.IsNullOrWhiteSpace(verb))
        {
            return false;
        }

        var name = verb.Trim().TrimStart('/').ToLowerInvariant();
        if (name.Length == 0)
        {
            return false;
        }

        foreach (var spec in BuiltIn)
        {
            if (name == spec.Name)
            {
                matched = spec.Verb;
                return true;
            }

            foreach (var alias in spec.Aliases)
            {
                if (name == alias)
                {
                    matched = spec.Verb;
                    return true;
                }
            }
        }

        return false;
    }
}
