using CrystalCode.Display.Composer;

namespace CrystalCode.Sessions;

/// <summary>
/// Nested slash completions for conversation exports.
/// </summary>
public static class ExportCompletions
{
    private static readonly IReadOnlyList<SlashOption> SystemFlag =
    [
        new("--system", "Include the live system prompt", ["--system"])
    ];

    public static IReadOnlyList<SlashOption> All { get; } =
    [
        new(
            "markdown",
            "Export Markdown; optional path and --system",
            ["markdown", "md"],
            SystemFlag,
            ArgumentsOptional: true,
            ArgumentsAfterValue: SystemFlag),
        new(
            "json",
            "Export JSON; optional path and --system",
            ["json"],
            SystemFlag,
            ArgumentsOptional: true,
            ArgumentsAfterValue: SystemFlag)
    ];
}
