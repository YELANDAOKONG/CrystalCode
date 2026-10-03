using CrystalCode.Engine.Approvals;

namespace CrystalCode.Engine.Events;

/// <summary>
/// Persistent session labels: model, workspace, mode, approval, and prompt set.
/// Thinking and prompt set are display labels; an empty prompt set means the default.
/// </summary>
public sealed record SessionChrome(
    string Model,
    string WorkspaceRoot,
    bool PlanMode,
    ApprovalMode Approval,
    string Thinking,
    string PromptSet);
