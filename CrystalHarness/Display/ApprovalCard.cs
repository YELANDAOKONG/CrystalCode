using Crystal.Tools;

using CrystalHarness.Approvals;

namespace CrystalHarness.Display;

/// <summary>
/// Codex-style permission card: full risk, authority, outcome, and rationale.
/// </summary>
public static class ApprovalCard
{
    public static string ActionLine(ToolCall call) =>
        ToolCallText.Summary(call.Name, call.Arguments);

    public static string HostLine(ToolClassification classification) =>
        "Risk  "
        + DisplayCase.Token(classification.Risk.Value)
        + "  ·  Authority  "
        + DisplayCase.Token(classification.Authority.Value);

    public static string ReviewLine(ApprovalReviewVerdict review) =>
        "Outcome  "
        + DisplayCase.Token(review.Outcome)
        + "  ·  Risk  "
        + DisplayCase.Token(review.RiskLevel.Value)
        + "  ·  Auth  "
        + DisplayCase.Token(review.UserAuthorization.Value);

    public static string PassLine(
        ToolClassification classification,
        ApprovalPassReason reason) =>
        "Allowed  ·  "
        + DisplayCase.Token(reason.Value)
        + "  ·  "
        + HostLine(classification);

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
            foreach (var line in SplitRationale(review.Rationale))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    public static IReadOnlyList<string> SplitRationale(string? rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale))
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var line in rationale.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
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
