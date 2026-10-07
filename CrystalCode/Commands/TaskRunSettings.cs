using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Process-only options for <c>crystal run</c>. Nothing here is written back
/// to <c>config.json</c>.
/// </summary>
public sealed class TaskRunSettings : SessionLaunchSettings
{
    [CommandArgument(0, "[TASK]")]
    public string? TaskText { get; init; }

    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--space")]
    public bool Space { get; init; }

    [CommandOption("--workspace-trust <on|off>")]
    public string? WorkspaceTrust { get; init; }

    [CommandOption("--show-thinking")]
    public bool ShowThinking { get; init; }

    [CommandOption("--format <default|json>")]
    public string? Format { get; init; }
}
