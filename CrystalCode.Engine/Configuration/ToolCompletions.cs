using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Slash argument completions for external-tool listing and policy controls.
/// </summary>
public static class ToolCompletions
{
    private static readonly IReadOnlyList<SlashCompletion> BuiltInTools =
        BuiltInToolSelection.Names
            .Select(name => new SlashCompletion(name, "Configure built-in tool", [name]))
            .ToArray();

    private static readonly IReadOnlyList<SlashCompletion> BuiltInToolActions =
    [
        new("show", "Show disabled tools for this mode", ["show"]),
        new("enable", "Enable a built-in tool for this mode", ["enable"], BuiltInTools),
        new("disable", "Disable a built-in tool for this mode", ["disable"], BuiltInTools)
    ];

    private static readonly IReadOnlyList<SlashCompletion> SessionModes =
    [
        new("show", "Show session-disabled tools", ["show"]),
        new("plan", "Configure Plan built-in tools for this session", ["plan"], BuiltInToolActions),
        new("work", "Configure Work built-in tools for this session", ["work"], BuiltInToolActions)
    ];

    private static readonly IReadOnlyList<SlashCompletion> BuiltInModes =
    [
        new("plan", "Configure Plan built-in tools", ["plan"], BuiltInToolActions),
        new("work", "Configure Work built-in tools", ["work"], BuiltInToolActions),
        new("session", "Disable built-in tools for this session only", ["session"], SessionModes)
    ];

    private static readonly IReadOnlyList<SlashCompletion> Policies =
    [
        new("author", "Follow author approval declarations", ["author"]),
        new("host", "Use host approval policy", ["host"])
    ];

    public static IReadOnlyList<SlashCompletion> All { get; } =
    [
        new("approval", "show Home and Project approval policy", ["approval"]),
        new("home", "set Home tool approval policy", ["home"], Policies),
        new("project", "set Project tool approval policy", ["project"], Policies),
        new("reload", "Reload external tools", ["reload"]),
        new("on", "Enable external tools", ["on"]),
        new("off", "Disable external tools", ["off"]),
        new("builtin", "Show or configure Plan and Work built-in tools", ["builtin"], BuiltInModes)
    ];
}
