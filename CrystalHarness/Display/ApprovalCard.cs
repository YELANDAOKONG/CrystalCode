using Crystal.Tools;

using CrystalHarness.Approvals;

namespace CrystalHarness.Display;

/// <summary>
/// Formats an OpenCode/Codex-style permission card without Demo panels.
/// </summary>
public static class ApprovalCard
{
    public static string ActionLine(ToolCall call) =>
        ToolCallText.Summary(call.Name, call.Arguments);

    public static string HostLine(ToolClassification classification) =>
        classification.Risk.Value + " · " + classification.Authority.Value;

    public static string ReviewLine(ApprovalReviewVerdict review) =>
        "review  " + review.Outcome;

    public static string PassLine(
        ToolCall call,
        ApprovalPassReason reason) =>
        reason == ApprovalPassReason.Review
            ? "allowed  review  " + ActionLine(call)
            : "allowed  " + ActionLine(call);

    public static IReadOnlyList<string> PassLines(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
        return [PassLine(call, reason)];
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
