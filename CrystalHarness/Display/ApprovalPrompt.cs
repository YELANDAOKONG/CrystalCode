using Crystal.Tools;

using CrystalHarness.Approvals;

namespace CrystalHarness.Display;

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
            ApprovalCard.PassLines(call, classification, reason, review));
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
        _renderer.SetOverlay(CardLines(call, classification, review));
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
        }
    }

    private static List<string> CardLines(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review)
    {
        var lines = new List<string>
        {
            ApprovalCard.ActionLine(call),
            ApprovalCard.HostLine(classification)
        };
        if (review is not null)
        {
            lines.Add(ApprovalCard.ReviewLine(review));
            lines.Add(review.Rationale);
        }

        lines.Add("y once  ·  s session  ·  a always  ·  n deny");
        return lines;
    }
}
