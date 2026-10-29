namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Built-in slash verbs, including aliases such as /new for /clear.
/// </summary>
public static class SlashCatalog
{
    public static IReadOnlyList<SlashSpec> BuiltIn { get; } =
    [
        new(SessionVerb.Help, "help", ["h"], "Shortcuts and commands"),
        new(SessionVerb.Plan, "plan", [], "toggle Plan / Work"),
        new(SessionVerb.Mode, "mode", [], "Show or select a mode"),
        new(SessionVerb.Approval, "approval", [],
            "default | edit | review | audit | full, model, thinking, tier, speed, or extended",
            ["default", "edit", "review", "audit", "full", "model", "thinking", "tier", "speed", "extended"]),
        new(SessionVerb.Thinking, "thinking", ["think", "effort"], "off | none | default | an effort token the model lists",
            ["off", "none", "default"]),
        new(SessionVerb.Context, "context", [], "Cycle or set the model context window"),
        new(SessionVerb.Tier, "tier", [], "Cycle or set the model service tier"),
        new(SessionVerb.Speed, "speed", ["fast"], "Cycle or set the model speed"),
        new(SessionVerb.Tokens, "tokens", [], "Toggle estimated progress tokens",
            ["on", "off"]),
        new(SessionVerb.Verbose, "verbose", [], "Tool, command, result, call, approval, review, or thinking detail",
            ["tools", "commands", "results", "calls", "approvals", "review", "thinking", "on", "off"]),
        new(SessionVerb.Model, "model", [], "Show or set provider and model"),
        new(SessionVerb.PromptSet, "promptset", ["prompts"], "Show or select prompt set"),
        new(SessionVerb.PromptAttachment, "promptattach", [],
            "List prompt attachments or change the enabled list",
            ["enable", "disable", "up", "down"]),
        new(SessionVerb.Status, "status", [], "Summary or full diagnostics", ["full"]),
        new(SessionVerb.Stats, "stats", [], "Token and tool usage summary", ["all", "7d", "30d", "tools"]),
        new(SessionVerb.Btw, "btw", ["side"], "Side question, not saved in the session"),
        new(SessionVerb.StatusLine, "statusline", [], "Show or configure the custom status line",
            ["on", "off", "reset"]),
        new(SessionVerb.Clear, "clear", ["new"], "New conversation"),
        new(SessionVerb.Cd, "cd", [], "Show or set workspace"),
        new(SessionVerb.Access, "access", [], "Session directory access: add, remove, or default",
            ["add", "remove", "default"]),
        new(SessionVerb.Space, "space", [], "Switch to the operator space"),
        new(SessionVerb.Trust, "trust", [], "Show workspace trust or turn it on or off",
            ["on", "off", "forget"]),
        new(SessionVerb.Rules, "rules", [], "Show or set global and project rule files",
            ["on", "off", "global", "project"]),
        new(SessionVerb.Notify, "notify", [], "Show operator notification, turn it on or off, or run a test",
            ["on", "off", "test"]),
        new(SessionVerb.Theme, "theme", [], "Show or select the color theme"),
        new(SessionVerb.Resume, "resume", ["continue"], "Choose a session, workspace, or all", ["all"]),
        new(SessionVerb.Fork, "fork", [], "Branch current conversation or ID"),
        new(SessionVerb.Agents, "agents", [], "List or configure child Agents", ["on", "off", "concurrency", "limit", "model", "thinking", "context", "tier", "speed"]),
        new(SessionVerb.Child, "child", [], "Inspect a child session or return to parent", ["parent"]),
        new(SessionVerb.Sessions, "sessions", [], "List workspace sessions or all", ["all"]),
        new(SessionVerb.Compact, "compact", ["summarize"],
            "Summarize earlier context, fold all of it with full, or set the model, thinking gear, tier, speed, summary display, and microcompaction",
            ["full", "model", "thinking", "tier", "speed", "summary", "auto", "micro"]),
        new(SessionVerb.Trim, "trim", [],
            "Leave old resources, such as images, out of model requests: a count to keep, all, or on and off for automatic trimming",
            ["all", "on", "off"]),
        new(SessionVerb.Todos, "todos", ["todo"], "Show the full session todo list, toggle the todo bar, or set its rows",
            ["bar", "rows"]),
        new(SessionVerb.Tools, "tools", [], "List tools or configure external tool sets",
            ["on", "off", "reload", "builtin", "enable", "disable", "show", "home", "project"]),
        new(SessionVerb.Plugins, "plugins", [], "List plugins or turn one directory on or off",
            ["on", "off", "reload", "enable", "disable", "show", "home", "project"]),
        new(SessionVerb.Skills, "skills", [], "List skills or turn the skill tool on or off",
            ["on", "off", "reload"]),
        new(SessionVerb.Mcp, "mcp", [], "List, show, or configure MCP servers",
            ["list", "show", "on", "off", "reload", "enable", "disable", "approval", "home", "project"]),
        new(SessionVerb.Attach, "attach", ["image"], "Attach a workspace image"),
        new(SessionVerb.Quick, "quick", ["shortcuts"], "Open quick prompts (F6 in the terminal)"),
        new(SessionVerb.Edit, "edit", ["editor"], "Open the full-screen editor (F8 in the terminal)"),
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
