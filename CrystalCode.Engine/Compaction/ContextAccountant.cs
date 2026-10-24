using Crystal;
using Crystal.Chat;

namespace CrystalCode.Engine.Compaction;

/// <summary>
/// Decides whether estimated or reported usage has crossed the compact limit.
/// The output reservation is capped by a share of the window, so a small
/// window keeps a usable input budget instead of reserving most of itself.
/// </summary>
public static class ContextAccountant
{
    public const int DefaultReservedTokens = 20_000;

    /// <summary>
    /// Tokens held back for the reply. An explicit value wins. Otherwise the
    /// reservation is the smaller of the default, the model's output cap, and
    /// a quarter of the window.
    /// </summary>
    public static int ReservedTokens(
        int contextWindow,
        int? maxTokens = null,
        int? reserved = null)
    {
        if (contextWindow <= 0)
        {
            return 0;
        }

        if (reserved is int declared && declared > 0)
        {
            return Math.Min(declared, Math.Max(1, contextWindow - 1));
        }

        var output = maxTokens is > 0 ? maxTokens.Value : DefaultReservedTokens;
        var share = Math.Max(1, contextWindow / 4);
        return Math.Max(1, Math.Min(Math.Min(DefaultReservedTokens, output), share));
    }

    public static int UsableTokens(
        int contextWindow,
        int? maxTokens = null,
        int? reserved = null)
    {
        if (contextWindow <= 0)
        {
            return 0;
        }

        return contextWindow - ReservedTokens(contextWindow, maxTokens, reserved);
    }

    public static int CompactLimit(
        int contextWindow,
        double threshold,
        int? maxTokens = null,
        int? reserved = null)
    {
        if (contextWindow <= 0 || threshold is <= 0 or > 1)
        {
            return 0;
        }

        var fraction = (int)Math.Floor(contextWindow * threshold);
        return Math.Min(fraction, UsableTokens(contextWindow, maxTokens, reserved));
    }

    /// <summary>
    /// Largest output a request may ask for. The prompt is capped at
    /// <see cref="CompactLimit"/>, so this leaves the rest of the window for
    /// the reply and input plus output never exceed the window. Zero when the
    /// model declares no output cap, which leaves the provider default alone.
    /// </summary>
    public static int OutputTokens(
        int contextWindow,
        double threshold,
        int? maxTokens,
        int? reserved = null)
    {
        if (contextWindow <= 0 || maxTokens is not > 0)
        {
            return 0;
        }

        var room = contextWindow - CompactLimit(contextWindow, threshold, maxTokens, reserved);
        return Math.Max(1, Math.Min(maxTokens.Value, Math.Max(1, room)));
    }

    public static bool ShouldCompact(
        TokenUsage? usage,
        int contextWindow,
        double threshold,
        int? maxTokens = null,
        int? reserved = null)
    {
        if (usage is null)
        {
            return false;
        }

        return ShouldCompact(usage.TotalTokenCount, contextWindow, threshold, maxTokens, reserved);
    }

    public static bool ShouldCompact(
        long estimatedTokens,
        int contextWindow,
        double threshold,
        int? maxTokens = null,
        int? reserved = null)
    {
        var limit = CompactLimit(contextWindow, threshold, maxTokens, reserved);
        return limit > 0 && estimatedTokens >= limit;
    }

    /// <summary>
    /// Decides from a transcript. When <paramref name="anchor"/> still points
    /// into it, the size is that provider report plus the items after it, and
    /// <paramref name="reportedUsage"/> is skipped because the anchor already
    /// holds it. Otherwise the whole transcript is estimated and the reported
    /// usage is checked beside it.
    /// </summary>
    public static bool ShouldCompact(
        IReadOnlyList<ChatItem> transcript,
        ContextAnchor? anchor,
        TokenUsage? reportedUsage,
        CompactionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(limits);
        var anchored = TokenEstimator.TryContext(transcript, anchor, out var tokens);
        return ShouldCompact(
            anchored ? tokens : TokenEstimator.Items(transcript),
            anchored ? null : reportedUsage,
            limits.ContextWindow,
            limits.Threshold,
            limits.MaxTokens,
            limits.ReservedOutputTokens);
    }

    public static bool ShouldCompact(
        long estimatedTokens,
        TokenUsage? reportedUsage,
        int contextWindow,
        double threshold,
        int? maxTokens = null,
        int? reserved = null) =>
        ShouldCompact(estimatedTokens, contextWindow, threshold, maxTokens, reserved)
        || ShouldCompact(reportedUsage, contextWindow, threshold, maxTokens, reserved);
}
