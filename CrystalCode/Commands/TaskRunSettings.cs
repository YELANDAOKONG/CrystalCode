using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Process-only options for <c>crystal run</c>. Nothing here is written back
/// to <c>config.json</c>.
/// </summary>
public sealed class TaskRunSettings : CommandSettings
{
    [CommandArgument(0, "[TASK]")]
    public string? TaskText { get; init; }

    [CommandOption("-p|--provider <PROVIDER>")]
    public string? Provider { get; init; }

    [CommandOption("-m|--model <MODEL>")]
    public string? Model { get; init; }

    [CommandOption("-w|--workspace <PATH>")]
    public string? Workspace { get; init; }

    [CommandOption("--space")]
    public bool Space { get; init; }

    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("--approval <MODE>")]
    public string? Approval { get; init; }

    [CommandOption("--approval-model <on|off>")]
    public string? ApprovalModel { get; init; }

    [CommandOption("--approval-provider <PROVIDER>")]
    public string? ApprovalProvider { get; init; }

    [CommandOption("--approval-model-id <MODEL>")]
    public string? ApprovalModelId { get; init; }

    [CommandOption("--plan")]
    public bool Plan { get; init; }

    [CommandOption("--work")]
    public bool Work { get; init; }

    [CommandOption("--thinking <EFFORT>")]
    public string? Thinking { get; init; }

    [CommandOption("--prompt-set <NAME>")]
    public string? PromptSet { get; init; }

    [CommandOption("--prompt-attachments <on|off>")]
    public string? PromptAttachments { get; init; }

    [CommandOption("--skills <on|off>")]
    public string? Skills { get; init; }

    [CommandOption("--external-tools <on|off>")]
    public string? ExternalTools { get; init; }

    [CommandOption("--plugins <on|off>")]
    public string? Plugins { get; init; }

    [CommandOption("--workspace-trust <on|off>")]
    public string? WorkspaceTrust { get; init; }

    [CommandOption("--model-calls <COUNT>")]
    public string? ModelCalls { get; init; }

    [CommandOption("--tool-calls <COUNT>")]
    public string? ToolCalls { get; init; }

    [CommandOption("--duration <SECONDS>")]
    public string? Duration { get; init; }

    [CommandOption("--bash-timeout <SECONDS>")]
    public string? BashTimeout { get; init; }

    [CommandOption("--show-thinking")]
    public bool ShowThinking { get; init; }

    [CommandOption("--format <default|json>")]
    public string? Format { get; init; }
}
