using Crystal.Chat;
using Crystal.Tools;

namespace CrystalHarness.Compaction;

/// <summary>
/// Clears old tool output while protecting recent turns and a recent token band.
/// </summary>
public static class ToolResultPruner
{
    public const int ProtectTokens = 40_000;

    public const int MinimumPruneTokens = 20_000;

    public static IReadOnlyList<ChatItem> Prune(
        IReadOnlyList<ChatItem> transcript,
        string omittedText,
        int protectTokens = ProtectTokens,
        int minimumPruneTokens = MinimumPruneTokens)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentException.ThrowIfNullOrWhiteSpace(omittedText);
        if (protectTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(protectTokens), protectTokens, "Protect tokens cannot be negative.");
        }

        if (minimumPruneTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumPruneTokens),
                minimumPruneTokens,
                "Minimum prune tokens cannot be negative.");
        }

        var total = 0;
        var pruned = 0;
        var toPrune = new HashSet<int>();
        var userTurns = 0;

        for (var i = transcript.Count - 1; i >= 0; i--)
        {
            if (transcript[i] is ChatMessage user && user.Role == ChatRole.User)
            {
                userTurns++;
            }

            if (userTurns < 2)
            {
                continue;
            }

            if (CompactionSelection.IsSummary(transcript[i]))
            {
                break;
            }

            if (transcript[i] is not ToolResult result
                || result.Status != ToolResultStatus.Success)
            {
                continue;
            }

            if (result.Text == omittedText)
            {
                break;
            }

            var estimate = TokenEstimator.Text(result.Text);
            total += estimate;
            if (total <= protectTokens)
            {
                continue;
            }

            pruned += estimate;
            toPrune.Add(i);
        }

        if (pruned <= minimumPruneTokens)
        {
            return transcript;
        }

        var next = new List<ChatItem>(transcript.Count);
        for (var i = 0; i < transcript.Count; i++)
        {
            if (toPrune.Contains(i) && transcript[i] is ToolResult result)
            {
                next.Add(new ToolResult(result.CallId, omittedText, result.Status));
                continue;
            }

            next.Add(transcript[i]);
        }

        return next;
    }
}
