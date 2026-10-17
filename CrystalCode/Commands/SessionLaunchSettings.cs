using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Session options shared by <c>crystal</c>, <c>crystal space</c>, and
/// <c>crystal run</c>. Values override the saved configuration for this
/// process. They are not written at startup.
/// </summary>
public class SessionLaunchSettings : CommandSettings
{
    [CommandOption("-p|--provider <PROVIDER>")]
    public string? Provider { get; init; }

    [CommandOption("-m|--model <MODEL>")]
    public string? Model { get; init; }

    [CommandOption("--home <PATH>")]
    public string? Home { get; init; }

    [CommandOption("--allow-read <PATH>")]
    public string[] AllowRead { get; init; } = [];

    [CommandOption("--allow-write <PATH>")]
    public string[] AllowWrite { get; init; } = [];

    [CommandOption("--approval <MODE>")]
    public string? Approval { get; init; }

    [CommandOption("--approval-model <on|off>")]
    public string? ApprovalModel { get; init; }

    [CommandOption("--approval-provider <PROVIDER>")]
    public string? ApprovalProvider { get; init; }

    [CommandOption("--approval-model-id <MODEL>")]
    public string? ApprovalModelId { get; init; }

    [CommandOption("--approval-thinking <EFFORT>")]
    public string? ApprovalThinking { get; init; }

    [CommandOption("--compaction-model <on|off>")]
    public string? CompactionModel { get; init; }

    [CommandOption("--compaction-provider <PROVIDER>")]
    public string? CompactionProvider { get; init; }

    [CommandOption("--compaction-model-id <MODEL>")]
    public string? CompactionModelId { get; init; }

    [CommandOption("--compaction-thinking <EFFORT>")]
    public string? CompactionThinking { get; init; }

    [CommandOption("--plan")]
    public bool Plan { get; init; }

    [CommandOption("--work")]
    public bool Work { get; init; }

    [CommandOption("--thinking <EFFORT>")]
    public string? Thinking { get; init; }

    [CommandOption("--context <BUDGET>")]
    public string? Context { get; init; }

    [CommandOption("--tier <TOKEN>")]
    public string? Tier { get; init; }

    [CommandOption("--speed|--fast <TOKEN>")]
    public string? Speed { get; init; }

    [CommandOption("--prompt-set <NAME>")]
    public string? PromptSet { get; init; }

    [CommandOption("--prompt-attachments <on|off>")]
    public string? PromptAttachments { get; init; }

    [CommandOption("--prompt-attachment <NAME>")]
    public string[] PromptAttachment { get; init; } = [];

    [CommandOption("--skills <on|off>")]
    public string? Skills { get; init; }

    [CommandOption("--external-tools <on|off>")]
    public string? ExternalTools { get; init; }

    [CommandOption("--plugins <on|off>")]
    public string? Plugins { get; init; }

    [CommandOption("--rules <VALUE>")]
    public string? Rules { get; init; }

    [CommandOption("--model-calls <COUNT>")]
    public string? ModelCalls { get; init; }

    [CommandOption("--tool-calls <COUNT>")]
    public string? ToolCalls { get; init; }

    [CommandOption("--duration <SECONDS>")]
    public string? Duration { get; init; }

    [CommandOption("--bash-timeout <SECONDS>")]
    public string? BashTimeout { get; init; }

    [CommandOption("--disable-tool <NAME>")]
    public string[] DisableTool { get; init; } = [];
}
