using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Events;

/// <summary>
/// Persistent session chrome: model, workspace, mode, approval, thinking,
/// prompt set, and selected local context budget in tokens.
/// Thinking and prompt set are display labels; an empty prompt set means the default.
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
    string? Mode = null);
