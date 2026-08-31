using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Approvals.Interfaces;
using CrystalHarness.Display.Shell;

namespace CrystalHarness.Display.Cards;

/// <summary>
/// Permission overlay. Keys, not a Live selection widget.
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
        _renderer.WriteApprovalPass(
            ApprovalCard.PassWidget(call, classification, reason, review));
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
        _renderer.SetOverlay(ApprovalCard.AskWidget(call, classification, review));
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = await _renderer.ReadKeyAsync(cancellationToken);
                if (ApprovalKeys.TryMap(key.Key, out var choice))
                {
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
