using Crystal;

namespace CrystalCode.Compaction;

/// <summary>
/// Decides whether estimated or reported usage has crossed the compact limit.
/// </summary>
public static class ContextAccountant
{
    public const int DefaultReservedTokens = 20_000;

    public static int ReservedTokens(int? maxTokens) =>
        Math.Min(DefaultReservedTokens, maxTokens is > 0 ? maxTokens.Value : DefaultReservedTokens);

    public static int UsableTokens(int contextWindow, int? maxTokens = null)
    {
        if (contextWindow <= 0)
        {
            return 0;
        }

        var reserved = ReservedTokens(maxTokens);
        if (reserved >= contextWindow)
        {
            return contextWindow;
        }

        return contextWindow - reserved;
    }

    public static int CompactLimit(int contextWindow, double threshold, int? maxTokens = null)
    {
        if (contextWindow <= 0 || threshold is <= 0 or > 1)
        {
            return 0;
        }

        var fraction = (int)Math.Floor(contextWindow * threshold);
        var reserved = ReservedTokens(maxTokens);
        if (reserved >= contextWindow)
        {
            return fraction;
        }

        return Math.Min(fraction, contextWindow - reserved);
    }

    public static bool ShouldCompact(
        TokenUsage? usage,
        int contextWindow,
        double threshold,
        int? maxTokens = null)
    {
        if (usage is null)
        {
            return false;
        }

        return ShouldCompact(usage.TotalTokenCount, contextWindow, threshold, maxTokens);
    }

    public static bool ShouldCompact(
        long estimatedTokens,
        int contextWindow,
        double threshold,
        int? maxTokens = null)
    {
        var limit = CompactLimit(contextWindow, threshold, maxTokens);
        return limit > 0 && estimatedTokens >= limit;
    }

    public static bool ShouldCompact(
        long estimatedTokens,
        TokenUsage? reportedUsage,
        int contextWindow,
        double threshold,
        int? maxTokens = null) =>
        ShouldCompact(estimatedTokens, contextWindow, threshold, maxTokens)
        || ShouldCompact(reportedUsage, contextWindow, threshold, maxTokens);
}
