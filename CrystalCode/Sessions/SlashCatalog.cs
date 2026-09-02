namespace CrystalCode.Sessions;

/// <summary>
/// Built-in slash verbs, including aliases such as /new for /clear.
/// </summary>
public static class SlashCatalog
{
    public static IReadOnlyList<SlashSpec> BuiltIn { get; } =
    [
        new(SessionVerb.Help, "help", ["h"], "Shortcuts and commands"),
        new(SessionVerb.Plan, "plan", [], "toggle Plan / Work"),
        new(SessionVerb.Approval, "approval", [], "default | edit | review | audit | full",
            ["default", "edit", "review", "audit", "full"]),
        new(SessionVerb.Thinking, "thinking", ["think"], "off | none | default | low | medium | high | maximum | max",
            ["off", "none", "default", "minimal", "low", "medium", "high", "maximum", "max"]),
        new(SessionVerb.Tokens, "tokens", [], "Toggle estimated progress tokens",
            ["on", "off"]),
        new(SessionVerb.Model, "model", [], "Show or set provider and model"),
        new(SessionVerb.PromptSet, "promptset", ["prompts"], "Show or select prompt set"),
        new(SessionVerb.Status, "status", [], "Summary or full diagnostics", ["full"]),
        new(SessionVerb.Clear, "clear", ["new"], "New conversation"),
        new(SessionVerb.Cd, "cd", [], "Show or set workspace"),
        new(SessionVerb.Resume, "resume", ["continue"], "Replay latest or ID"),
        new(SessionVerb.Fork, "fork", [], "Branch current conversation or ID"),
        new(SessionVerb.Sessions, "sessions", [], "List workspace sessions or all", ["all"]),
        new(SessionVerb.Compact, "compact", ["summarize"], "Summarize older context now"),
        new(SessionVerb.Todos, "todos", ["todo"], "Show the full session todo list"),
        new(SessionVerb.Tools, "tools", [], "List tools or configure external tools"),
        new(SessionVerb.Export, "export", [], "Export markdown, json, or show usage"),
        new(SessionVerb.Quit, "quit", ["exit", "q"], "Exit")
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
