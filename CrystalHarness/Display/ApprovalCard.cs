using Crystal.Tools;

using CrystalHarness.Approvals;

namespace CrystalHarness.Display;

/// <summary>
/// Formats an OpenCode/Codex-style permission card without Demo panels.
/// </summary>
public static class ApprovalCard
{
    public static string ActionLine(ToolCall call) =>
        call.Name + "  " + CompactArguments(call.Arguments);

    public static string HostLine(ToolClassification classification) =>
        classification.Risk.Value + "  " + classification.Authority.Value;

    public static string ReviewLine(ApprovalReviewVerdict review) =>
        review.Outcome
        + "  risk "
        + review.RiskLevel.Value
        + "  auth "
        + review.UserAuthorization.Value;

    public static string PassLine(
        ToolClassification classification,
        ApprovalPassReason reason) =>
        "auto  "
        + reason.Value
        + "  risk "
        + classification.Risk.Value
        + "  auth "
        + classification.Authority.Value;

    public static IReadOnlyList<string> PassLines(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
        var lines = new List<string>
        {
            ActionLine(call),
            PassLine(classification, reason)
        };
        if (!string.IsNullOrWhiteSpace(classification.Summary))
        {
            lines.Add(classification.Summary);
        }

        if (review is not null)
        {
            lines.Add(ReviewLine(review));
            lines.Add(review.Rationale);
        }

        return lines;
    }

    public static string CompactArguments(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var flat = arguments
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (flat is "{}" or "")
        {
            return string.Empty;
        }

        return flat.Length <= 80 ? flat : flat[..77] + "...";
    }
}
