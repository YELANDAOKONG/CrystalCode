using Crystal;

namespace CrystalCode.Sessions;

/// <summary>
/// Context and elapsed labels for the status bar.
/// </summary>
public static class UsageText
{
    public static string Format(TokenUsage? usage, int contextWindow)
    {
        if (usage is null)
        {
            return "CTX --";
        }

        var percent = contextWindow <= 0
            ? 0
            : Math.Clamp((int)(usage.TotalTokenCount * 100 / contextWindow), 0, 99);
        return $"CTX {percent}%  ·  {FormatNumber(usage.InputTokenCount)} IN / {FormatNumber(usage.OutputTokenCount)} OUT";
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
