using Crystal.Tools;

using CrystalCode.Approvals.Interfaces;
using CrystalCode.Sessions;

namespace CrystalCode.Approvals;

/// <summary>
/// Permission overlay. Keys share the session frame loop; not a Live widget.
/// </summary>
public sealed class ApprovalPrompt : IApprovalPrompt
{
    private readonly SessionRenderer _renderer;

    public ApprovalPrompt(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
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
        _renderer.SetProgress(ProgressText.Running(call.Name));
        _renderer.WriteApprovalPass(
            ApprovalCard.PassWidget(call, classification, reason, review));
    }

    public void NotifyReviewing(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        _renderer.SetProgress(ProgressText.Reviewing);
    }

    public async ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        _renderer.CloseStream();
        _renderer.PauseComposer();
        _renderer.SetProgress(ProgressText.AwaitingApproval);
        _renderer.SetOverlay(ApprovalCard.AskWidget(call, classification, review));
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = await _renderer.ReadKeyAsync(cancellationToken);
                if (ApprovalKeys.TryMap(key.Key, out var choice))
                {
                    if (choice.IsAllow)
                    {
                        _renderer.SetProgress(ProgressText.Running(call.Name));
                    }
                    else
                    {
                        _renderer.SetProgress(ProgressText.WaitingForModel);
                    }

                    return choice;
                }
            }
        }
        finally
        {
            _renderer.ClearOverlay();
            _renderer.ResumeComposer();
        }
    }
}
