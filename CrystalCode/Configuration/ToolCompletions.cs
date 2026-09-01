using CrystalCode.Display.Composer;

namespace CrystalCode.Configuration;

/// <summary>
/// Slash argument completions for external-tool listing and policy controls.
/// </summary>
public static class ToolCompletions
{
    private static readonly IReadOnlyList<SlashOption> Policies =
    [
        new("author", "follow author approval declarations", ["author"]),
        new("host", "use host approval policy", ["host"])
    ];

    public static IReadOnlyList<SlashOption> All { get; } =
    [
        new("approval", "show Home and Project approval policy", ["approval"]),
        new("home", "set Home tool approval policy", ["home"], Policies),
        new("project", "set Project tool approval policy", ["project"], Policies),
        new("reload", "reload external tools", ["reload"]),
        new("on", "enable external tools", ["on"]),
        new("off", "disable external tools", ["off"])
    ];
}
