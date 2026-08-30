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
