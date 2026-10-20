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
        int? summaryWindow = null,
        int? reservedOutputTokens = null,
        int? summaryMaxTokens = null,
        bool useSummaryMaxTokens = false)
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

        if (reservedOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedOutputTokens),
                reservedOutputTokens,
                "Reserved output tokens must be positive.");
        }

        if (useSummaryMaxTokens && summaryMaxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(summaryMaxTokens),
                summaryMaxTokens,
                "Summary maximum token count must be positive.");
        }

        ContextWindow = contextWindow;
        MaxTokens = maxTokens;
        Threshold = threshold;
        TailBudget = tailBudget;
        SummaryWindow = summaryWindow;
        ReservedOutputTokens = reservedOutputTokens;
        SummaryMaxTokens = summaryMaxTokens;
        UseSummaryMaxTokens = useSummaryMaxTokens;
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

    public int? ReservedOutputTokens { get; }

    /// <summary>
    /// Output cap of the model that writes the summary, when
    /// <see cref="UseSummaryMaxTokens"/> is set. Null means that model declares
    /// no cap. Ignored when the session model writes the summary.
    /// </summary>
    public int? SummaryMaxTokens { get; }

    public bool UseSummaryMaxTokens { get; }

    public int ResolveTailBudget()
    {
        if (TailBudget is int specified)
        {
            return specified;
        }

        var usable = ContextAccountant.UsableTokens(ContextWindow, MaxTokens, ReservedOutputTokens);
        if (usable <= 0)
        {
            usable = ContextWindow;
        }

        return Math.Clamp(
            (int)Math.Floor(usable * 0.25),
            MinPreserveRecentTokens,
            MaxPreserveRecentTokens);
    }

    /// <summary>
    /// Output cap for the summary request. The prompt budget reserves the same
    /// number, so the summary call fits its window and the stored summary stays
    /// under the compact ceiling.
    /// </summary>
    public int SummaryOutputCap()
    {
        var window = EffectiveSummaryWindow();
        var writerCap = UseSummaryMaxTokens ? SummaryMaxTokens : MaxTokens;
        var output = Math.Min(writerCap ?? SummaryOutputTokens, SummaryOutputTokens);
        // A window smaller than the summary slot cannot reserve the whole slot
        // and still send the conversation. Leave at least half the window for the prompt.
        output = Math.Min(output, Math.Max(1, window / 2));
        // The summary must land below the compact ceiling, or the next round
        // compacts what the last round just produced.
        var ceiling = ContextAccountant.CompactLimit(
            window,
            Threshold,
            MaxTokens,
            ReservedOutputTokens);
        return Math.Min(output, Math.Max(1, ceiling / 4));
    }

    public int SummaryPromptBudget() =>
        Math.Max(1, EffectiveSummaryWindow() - SummaryOutputCap());

    private int EffectiveSummaryWindow() =>
        SummaryWindow is int declared
            ? Math.Min(ContextWindow, declared)
            : ContextWindow;
}
