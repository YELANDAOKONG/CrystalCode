using Crystal;

namespace CrystalHarness.Compaction;

/// <summary>
/// Decides whether reported usage has crossed the selected model window.
/// </summary>
public static class ContextAccountant
{
    public static bool ShouldCompact(
        TokenUsage? usage,
        int contextWindow,
        double threshold)
    {
        if (usage is null || contextWindow <= 0 || threshold is <= 0 or > 1)
        {
            return false;
        }

        var limit = (long)Math.Floor(contextWindow * threshold);
        return usage.TotalTokenCount >= limit;
    }
}
