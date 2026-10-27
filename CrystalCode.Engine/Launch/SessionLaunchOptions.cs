namespace CrystalCode.Engine.Launch;

/// <summary>
/// Process-only session options a front end received at launch. Values
/// override the saved configuration in memory and are never written to
/// <c>config.json</c>. Text values keep the operator's spelling; parsing and
/// validation happen in <see cref="SessionLaunchOverrides"/>.
/// </summary>
public sealed record SessionLaunchOptions
{
    public string? Provider { get; init; }

    public string? Model { get; init; }

    public string? Home { get; init; }

    public IReadOnlyList<string> AllowRead { get; init; } = [];

    public IReadOnlyList<string> AllowWrite { get; init; } = [];

    public string? Approval { get; init; }

    public string? ApprovalModel { get; init; }

    public string? ApprovalProvider { get; init; }

    public string? ApprovalModelId { get; init; }

    public string? ApprovalThinking { get; init; }

    public string? ApprovalTier { get; init; }

    public string? ApprovalSpeed { get; init; }

    public string? ExtendedApproval { get; init; }

    public string? ExtendedApprovalProvider { get; init; }

    public string? ExtendedApprovalModelId { get; init; }

    public string? ExtendedApprovalThinking { get; init; }

    public string? ExtendedApprovalTier { get; init; }

    public string? ExtendedApprovalSpeed { get; init; }

    public string? ExtendedApprovalTrigger { get; init; }

    public string? ExtendedApprovalPrimary { get; init; }

    public string? CompactionModel { get; init; }

    public string? CompactionProvider { get; init; }

    public string? CompactionModelId { get; init; }

    public string? CompactionThinking { get; init; }

    public bool Plan { get; init; }

    public bool Work { get; init; }

    public string? Mode { get; init; }

    public string? Thinking { get; init; }

    public string? Context { get; init; }

    public string? Tier { get; init; }

    public string? Speed { get; init; }

    public string? PromptSet { get; init; }

    public string? PromptAttachments { get; init; }

    public IReadOnlyList<string> PromptAttachment { get; init; } = [];

    public string? Agents { get; init; }

    public int? AgentsConcurrency { get; init; }

    public string? AgentsModelCalls { get; init; }

    public string? AgentsToolCalls { get; init; }

    public string? AgentsDuration { get; init; }

    public string? Skills { get; init; }

    public string? ExternalTools { get; init; }

    public string? Mcp { get; init; }

    public string? Plugins { get; init; }

    public string? Rules { get; init; }

    /// <summary>
    /// Theme name for this process only. Null keeps the saved value.
    /// </summary>
    public string? Theme { get; init; }

    /// <summary>
    /// <c>on</c> or <c>off</c> for the <c>workspaceTrust</c> preference in
    /// this process, including a later move to another directory. Null keeps
    /// the saved value.
    /// </summary>
    public string? WorkspaceTrust { get; init; }

    public string? ModelCalls { get; init; }

    public string? ToolCalls { get; init; }

    public string? Duration { get; init; }

    public string? BashTimeout { get; init; }

    public IReadOnlyList<string> DisableTool { get; init; } = [];
}
