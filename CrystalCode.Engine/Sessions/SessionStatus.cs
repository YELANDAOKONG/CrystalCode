using Crystal;

using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Immutable snapshot rendered by the status command.
/// </summary>
public sealed record SessionStatus(
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
    TokenUsage? CumulativeUsage,
    bool CustomStatusLineEnabled = false,
    string? ApprovalModel = null,
    bool VerboseApprovalsEnabled = true,
    bool VerboseThinkingEnabled = true,
    bool VerboseReviewEnabled = true,
    bool VerboseResultsEnabled = true,
    bool VerboseCallsEnabled = true,
    bool PluginsEnabled = true,
    int Plugins = 0,
    string? ApprovalThinking = null,
    string? Retry = null,
    string? ApprovalRetry = null,
    string? CompactionModel = null,
    string? MicroCompaction = null,
    bool GlobalRulesEnabled = true,
    bool ProjectRulesEnabled = true,
    string ServiceTier = "",
    string Speed = "",
    bool McpEnabled = true,
    int McpServers = 0,
    int McpTools = 0);
