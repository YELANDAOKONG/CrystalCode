using Crystal;

namespace CrystalCode.Sessions;

/// <summary>
/// Context and elapsed labels for the status bar.
/// </summary>
public static class UsageText
{
    public static string Format(TokenUsage? usage, int contextWindow)
        => Format(usage, usage, contextWindow);

    public static string Format(
        TokenUsage? contextUsage,
        TokenUsage? cumulativeUsage,
        int contextWindow)
    {
        var context = FormatContext(contextUsage, contextWindow);
        if (cumulativeUsage is null)
        {
            return context;
        }

        return $"{context}  ·  {FormatNumber(cumulativeUsage.InputTokenCount)} IN / "
            + $"{FormatNumber(cumulativeUsage.OutputTokenCount)} OUT";
    }

    private static string FormatContext(TokenUsage? usage, int contextWindow)
    {
        if (usage is null)
        {
            return "CTX --";
        }

        var percent = contextWindow <= 0
            ? 0
            : Math.Clamp((int)((double)usage.TotalTokenCount / contextWindow * 100), 0, 100);
        return $"CTX {percent}%";
    }

    public static string FormatTotal(TokenUsage? usage) =>
        usage is null ? string.Empty : FormatNumber(usage.TotalTokenCount) + " Total";

    public static string FormatEstimate(int tokens)
    {
        if (tokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tokens), tokens, "Token estimate cannot be negative.");
        }

        return "~" + FormatNumber(tokens) + " Tokens";
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 10)
        {
            return $"{elapsed.TotalSeconds:0.0}s";
        }

        return $"{(int)elapsed.TotalSeconds}s";
    }

    private static string FormatNumber(long value)
    {
        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000.0:0.#}M";
        }

        if (value >= 10_000)
        {
            return $"{value / 1_000.0:0.#}k";
        }

        return value.ToString();
    }
}
