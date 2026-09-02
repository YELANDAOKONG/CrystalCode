using Crystal;

using CrystalCode.Approvals;

namespace CrystalCode.Sessions;

/// <summary>
/// Immutable snapshot rendered by the status command.
/// </summary>
internal sealed record SessionStatus(
    string SessionId,
    DateTimeOffset StartedUtc,
    string WorkspaceRoot,
    bool PlanMode,
    ApprovalMode Approval,
    string Thinking,
    string PromptSet,
    string Provider,
    string Model,
    int ContextWindow,
    TokenUsage? Usage,
    int UserTurns,
    int ModelCalls,
    int ToolCalls,
    int QueuedMessages,
    int Todos,
    bool SkillsEnabled,
    bool ExternalToolsEnabled,
    bool EstimatedTokensEnabled,
    bool VerboseToolsEnabled,
    bool VerboseCommandsEnabled,
    int PlanTools,
    int WorkTools,
    int ExternalTools,
    TokenUsage? CumulativeUsage);
