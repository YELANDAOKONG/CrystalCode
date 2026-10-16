using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Compaction;

/// <summary>
/// Window, reserved output, and retained-tail budget for one compaction.
/// </summary>
public sealed record CompactionLimits
{
    public const int MinPreserveRecentTokens = 2_000;

    public const int MaxPreserveRecentTokens = 32_000;

    public const int SummaryOutputTokens = 16_384;

    public CompactionLimits(
        int contextWindow,
        int? maxTokens = null,
        double threshold = HarnessSettings.DefaultCompactionThreshold,
        int? tailBudget = null,
        int? summaryWindow = null)
    {
        if (contextWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contextWindow),
                contextWindow,
                "Context window must be positive.");
        }

        if (threshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                threshold,
                "Compaction threshold must be greater than 0 and at most 1.");
        }

        if (maxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTokens),
                maxTokens,
                "Maximum token count must be positive.");
        }

        if (tailBudget is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tailBudget),
                tailBudget,
                "Tail budget cannot be negative.");
        }

        if (summaryWindow is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(summaryWindow),
                summaryWindow,
                "Summary window must be positive.");
        }

        ContextWindow = contextWindow;
        MaxTokens = maxTokens;
        Threshold = threshold;
        TailBudget = tailBudget;
        SummaryWindow = summaryWindow;
    }

    public int ContextWindow { get; }

    public int? MaxTokens { get; }

    public double Threshold { get; }

    public int? TailBudget { get; }

    /// <summary>
    /// Declared window of a dedicated summarizer. The summary prompt uses the
    /// smaller of this value and <see cref="ContextWindow"/>. Tail and threshold
    /// stay on the session window.
    /// </summary>
    public int? SummaryWindow { get; }

    public int ResolveTailBudget()
    {
        if (TailBudget is int specified)
        {
            return specified;
        }

        var usable = ContextAccountant.UsableTokens(ContextWindow, MaxTokens);
        if (usable <= 0)
        {
            usable = ContextWindow;
        }

        return Math.Clamp(
            (int)Math.Floor(usable * 0.25),
            MinPreserveRecentTokens,
            MaxPreserveRecentTokens);
    }

    public int SummaryPromptBudget()
    {
        var window = SummaryWindow is int declared
            ? Math.Min(ContextWindow, declared)
            : ContextWindow;
        var output = Math.Min(MaxTokens ?? SummaryOutputTokens, SummaryOutputTokens);
        // A window smaller than the summary slot cannot reserve the whole slot
        // and still send the conversation. Leave at least half the window for the prompt.
        output = Math.Min(output, Math.Max(1, window / 2));
        return Math.Max(1, window - output);
    }
}
