using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Events;

/// <summary>
/// Persistent session chrome: provider, model, workspace, mode, approval, thinking,
/// prompt set, selected local context budget in tokens, and the session id.
/// Thinking and prompt set are display labels; an empty prompt set means the default.
/// Every field is the active value. <see cref="Pending"/> lists values chosen
/// during a turn that apply on the next turn. <see cref="Reviewer"/> and
/// <see cref="ExtendedApproval"/> are null while that reviewer is off.
/// </summary>
public sealed record SessionChrome(
    string Model,
    string WorkspaceRoot,
    bool PlanMode,
    ApprovalMode Approval,
    string Thinking,
    string PromptSet,
    int ContextWindow = 0,
    string ServiceTier = "",
    string Speed = "",
    bool AgentsEnabled = false,
    string? Mode = null,
    string Provider = "",
    string SessionId = "",
    SessionPendingSettings? Pending = null,
    ReviewerChrome? Reviewer = null,
    ExtendedApprovalChrome? ExtendedApproval = null);
