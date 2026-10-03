using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Approvals.Interfaces;

namespace CrystalCode.Run;

/// <summary>
/// Denies every call that would have asked the operator. Review and Audit
/// still judge calls before this prompt is reached. Auto-pass and review
/// notifications are recorded by the session and are not denials.
/// </summary>
internal sealed class UnattendedApprovalPrompt : IApprovalPrompt
{
    public int Asked { get; private set; }

    public ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        cancellationToken.ThrowIfCancellationRequested();
        Asked++;
        return ValueTask.FromResult(ApprovalChoice.Deny);
    }

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
    }

    public void NotifyReviewing(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
    }
}
