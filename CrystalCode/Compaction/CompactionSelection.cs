using Crystal.Chat;
using Crystal.Tools;
using CrystalCode.Prompts;

namespace CrystalCode.Compaction;

/// <summary>
/// Splits a transcript into older history to summarize and a verbatim tail.
/// </summary>
public static class CompactionSelection
{
    public static bool IsSummary(ChatItem item) =>
        item is ChatMessage message && IsSummary(message);

    public static bool IsSummary(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Role == ChatRole.System
            && message.Text.StartsWith(CompactionPrompt.Marker, StringComparison.Ordinal);
    }

    public static string? LastSummaryBody(IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is ChatMessage message && IsSummary(message))
            {
                return message.Text[CompactionPrompt.Marker.Length..].Trim();
            }
        }

        return null;
    }

    public static CompactionSplit Choose(IReadOnlyList<ChatItem> items, int tailBudget)
    {
        ArgumentNullException.ThrowIfNull(items);
        var previous = LastSummaryBody(items);
        var working = new List<ChatItem>();
        for (var i = 0; i < items.Count; i++)
        {
            if (IsLiveSystem(items, i) || IsSummary(items[i]))
            {
                continue;
            }

            working.Add(items[i]);
        }

        if (working.Count == 0)
        {
            return new CompactionSplit([], [], previous);
        }

        var split = TailStart(working, tailBudget);
        return new CompactionSplit(
            working.GetRange(0, split),
            working.GetRange(split, working.Count - split),
            previous);
    }

    private static bool IsLiveSystem(IReadOnlyList<ChatItem> items, int index) =>
        index == 0
        && items[0] is ChatMessage message
        && message.Role == ChatRole.System
        && !IsSummary(message);

    private static int TailStart(IReadOnlyList<ChatItem> working, int tailBudget)
    {
        if (tailBudget <= 0)
        {
            return working.Count;
        }

        var turns = UserStarts(working);
        if (turns.Count == 0)
        {
            return SuffixStart(working, 0, working.Count, tailBudget);
        }

        var total = 0;
        var keepStart = working.Count;
        for (var t = turns.Count - 1; t >= 0; t--)
        {
            var start = turns[t];
            var end = t + 1 < turns.Count ? turns[t + 1] : working.Count;
            var size = TokenEstimator.Range(working, start, end - start);
            if (total + size <= tailBudget)
            {
                total += size;
                keepStart = start;
                continue;
            }

            var split = SuffixStart(working, start, end, tailBudget - total);
            if (split < end && split > start)
            {
                keepStart = split;
            }

            break;
        }

        return keepStart;
    }

    private static List<int> UserStarts(IReadOnlyList<ChatItem> working)
    {
        var starts = new List<int>();
        for (var i = 0; i < working.Count; i++)
        {
            if (working[i] is ChatMessage { Role.Value: "user" })
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    private static int SuffixStart(IReadOnlyList<ChatItem> working, int start, int end, int budget)
    {
        if (budget <= 0 || end - start <= 1)
        {
            return end;
        }

        for (var i = start + 1; i < end; i++)
        {
            if (working[i] is ToolResult)
            {
                continue;
            }

            if (TokenEstimator.Range(working, i, end - i) <= budget)
            {
                return i;
            }
        }

        return end;
    }
}
